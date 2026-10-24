using BotWatch.Domain;
using BotWatch.Monitoring.Domain;

namespace BotWatch.Monitoring.Tests;

public class MonitorTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    [Theory]
    [InlineData("https://example.com/", "https://example.com")]
    [InlineData("https://example.com/api/?x=1", "https://example.com/api")]
    [InlineData(" http://localhost:8080 ", "http://localhost:8080")]
    public void Http_target_is_normalized(string target, string expected)
    {
        Monitor.Create("Site", MonitorKind.Http, target, Now).Target.ShouldBe(expected);
    }

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("example.com")]
    [InlineData("")]
    public void Invalid_http_target_is_rejected(string target)
    {
        Should.Throw<DomainException>(() => Monitor.Create("Site", MonitorKind.Http, target, Now));
    }

    [Theory]
    [InlineData("my_test_bot", "@my_test_bot")]
    [InlineData("@SomeBot", "@SomeBot")]
    public void Bot_username_is_normalized(string target, string expected)
    {
        Monitor.Create("Bot", MonitorKind.TelegramBot, target, Now).Target.ShouldBe(expected);
    }

    [Fact]
    public void Bot_username_must_end_with_bot()
    {
        Should.Throw<DomainException>(() => Monitor.Create("Bot", MonitorKind.TelegramBot, "@durov", Now));
    }

    [Fact]
    public void Http_interval_has_lower_limit()
    {
        var monitor = CreateSite();

        Should.Throw<DomainException>(() =>
            monitor.AddHttpCheck("Health", TimeSpan.FromSeconds(10), Settings(timeoutSeconds: 5), Now));
    }

    [Fact]
    public void Timeout_must_be_shorter_than_interval()
    {
        var monitor = CreateSite();

        Should.Throw<DomainException>(() =>
            monitor.AddHttpCheck("Health", TimeSpan.FromSeconds(30), Settings(timeoutSeconds: 30), Now));
    }

    [Fact]
    public void Http_check_cannot_be_added_to_bot_monitor()
    {
        var monitor = Monitor.Create("Bot", MonitorKind.TelegramBot, "@test_bot", Now);

        Should.Throw<DomainException>(() => monitor.AddHttpCheck("Health", Minute, Settings(), Now));
    }

    [Fact]
    public void New_check_runs_immediately_then_by_interval()
    {
        var monitor = CreateSite();
        var check = monitor.AddHttpCheck("Health", Minute, Settings(path: "/health"), Now);
        monitor.ClearDomainEvents();

        monitor.ScheduleDueChecks(Now).ShouldBe(1);

        var job = monitor.DomainEvents.OfType<HttpCheckDue>().Single();
        job.Url.ShouldBe("https://example.com/health");
        job.ExpiresAt.ShouldBe(Now + Minute);
        check.NextRunAt.ShouldBe(Now + Minute);

        monitor.ScheduleDueChecks(Now.AddSeconds(59)).ShouldBe(0);
        monitor.ScheduleDueChecks(Now + Minute).ShouldBe(1);
    }

    [Fact]
    public void Disabled_monitor_does_not_schedule_checks()
    {
        var monitor = CreateSite();
        monitor.AddHttpCheck("Health", Minute, Settings(), Now);
        monitor.Update("Site", "https://example.com", enabled: false);

        monitor.ScheduleDueChecks(Now).ShouldBe(0);
    }

    [Fact]
    public void Disabling_monitor_propagates_to_checks()
    {
        var monitor = CreateSite();
        monitor.AddHttpCheck("Health", Minute, Settings(), Now);
        monitor.ClearDomainEvents();

        monitor.Update("Site", "https://example.com", enabled: false);

        monitor.DomainEvents.OfType<CheckConfigured>().Single().Enabled.ShouldBeFalse();
    }

    [Fact]
    public void Shorter_interval_pulls_next_run_closer()
    {
        var monitor = CreateSite();
        var check = monitor.AddHttpCheck("Health", TimeSpan.FromHours(1), Settings(), Now);
        monitor.ScheduleDueChecks(Now);

        monitor.UpdateHttpCheck(check.Id, "Health", Minute, Settings(), enabled: true, Now.AddSeconds(10));

        check.NextRunAt.ShouldBe(Now.AddSeconds(10) + Minute);
    }

    [Fact]
    public void Removing_check_raises_event()
    {
        var monitor = CreateSite();
        var check = monitor.AddHttpCheck("Health", Minute, Settings(), Now);

        monitor.RemoveCheck(check.Id);

        monitor.Checks.ShouldBeEmpty();
        monitor.DomainEvents.OfType<CheckDeleted>().Single().CheckId.ShouldBe(check.Id);
    }

    private static Monitor CreateSite() => Monitor.Create("Site", MonitorKind.Http, "https://example.com", Now);

    private static HttpCheckSettings Settings(string? path = null, int timeoutSeconds = 10)
    {
        return HttpCheckSettings.Create(path, "GET", 200, bodyContains: null, TimeSpan.FromSeconds(timeoutSeconds));
    }
}
