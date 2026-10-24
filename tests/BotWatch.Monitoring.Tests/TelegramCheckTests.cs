using BotWatch.Domain;
using BotWatch.Monitoring.Domain;

namespace BotWatch.Monitoring.Tests;

public class TelegramCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    [Fact]
    public void Command_gets_slash()
    {
        var scenario = TelegramScenario.Create([new(StepKind.SendCommand, Text: "status"), Reply()]);

        scenario.Steps[0].Text.ShouldBe("/status");
    }

    [Fact]
    public void Scenario_without_expect_reply_is_rejected()
    {
        Should.Throw<DomainException>(() => TelegramScenario.Create([new(StepKind.SendText, Text: "hi")]));
    }

    [Fact]
    public void Scenario_must_start_with_sending()
    {
        Should.Throw<DomainException>(() => TelegramScenario.Create([Reply(), new(StepKind.SendText, Text: "hi")]));
    }

    [Fact]
    public void Click_needs_reply_before_it()
    {
        Should.Throw<DomainException>(() => TelegramScenario.Create(
        [
            new(StepKind.SendCommand, Text: "/menu"),
            new(StepKind.ClickButton, Text: "Status"),
            Reply()
        ]));
    }

    [Fact]
    public void Invalid_regex_is_rejected()
    {
        Should.Throw<DomainException>(() => TelegramScenario.Create(
        [
            new(StepKind.SendText, Text: "hi"),
            new(StepKind.ExpectReply, Text: "([", TimeoutSeconds: 30, Condition: ReplyCondition.Regex)
        ]));
    }

    [Fact]
    public void Voice_must_be_ogg()
    {
        Should.Throw<DomainException>(() => TelegramScenario.Create(
        [
            new(StepKind.SendVoice, Voice: [0x49, 0x44, 0x33, 0x04]),
            Reply()
        ]));
    }

    [Fact]
    public void Messages_per_run_counts_sends_and_clicks()
    {
        var scenario = TelegramScenario.Create(
        [
            new(StepKind.SendCommand, Text: "/menu"),
            Reply(),
            new(StepKind.ClickButton, Text: "Status"),
            Reply(),
            new(StepKind.SendRandom, Phrases: ["hi", "hello", "hi"]),
            Reply()
        ]);

        scenario.MessagesPerRun.ShouldBe(3);
        scenario.Steps[4].Phrases!.Count.ShouldBe(2);
    }

    [Fact]
    public void Telegram_interval_is_at_least_five_minutes()
    {
        var monitor = CreateBot();

        Should.Throw<DomainException>(() =>
            monitor.AddTelegramCheck("Status", TimeSpan.FromMinutes(1), Guid.NewGuid(), StatusScenario(), Now));
    }

    [Fact]
    public void Scenario_must_fit_into_interval()
    {
        var monitor = CreateBot();
        var slow = TelegramScenario.Create(
        [
            new(StepKind.SendText, Text: "a"),
            Reply(300),
            new(StepKind.SendText, Text: "b"),
            Reply(300)
        ]);

        Should.Throw<DomainException>(() => monitor.AddTelegramCheck("Slow", FiveMinutes, Guid.NewGuid(), slow, Now));
    }

    [Fact]
    public void Http_monitor_does_not_accept_telegram_checks()
    {
        var site = Monitor.Create("Site", MonitorKind.Http, "https://example.com", Now);

        Should.Throw<DomainException>(() => site.AddTelegramCheck("Status", FiveMinutes, Guid.NewGuid(), StatusScenario(), Now));
    }

    [Fact]
    public void Budget_counts_runs_per_hour()
    {
        // раз в 5 минут по 2 сообщения = 24 в час, ещё одна такая проверка - 48
        var check = (2, FiveMinutes);

        Should.NotThrow(() => TelegramAccount.EnsureFitsBudget([check]));
        Should.Throw<DomainException>(() => TelegramAccount.EnsureFitsBudget([check, check]));
    }

    [Fact]
    public void Due_check_is_scheduled_to_account_queue_and_spends_budget()
    {
        var account = ActiveAccount();
        var monitor = CreateBot();
        monitor.AddTelegramCheck("Status", FiveMinutes, account.Id, StatusScenario(), Now);
        monitor.ClearDomainEvents();

        monitor.ScheduleDueChecks(Now, Accounts(account)).ShouldBe(1);

        var due = monitor.DomainEvents.OfType<TelegramCheckDue>().ShouldHaveSingleItem();
        due.AccountId.ShouldBe(account.Id);
        due.BotUsername.ShouldBe("@status_test_bot");
        account.BudgetUsed.ShouldBe(1);
    }

    [Fact]
    public void Check_waits_until_account_is_logged_in()
    {
        var account = TelegramAccount.Create("Probe", Now);
        var monitor = CreateBot();
        var check = monitor.AddTelegramCheck("Status", FiveMinutes, account.Id, StatusScenario(), Now);

        monitor.ScheduleDueChecks(Now, Accounts(account)).ShouldBe(0);

        check.NextRunAt.ShouldBe(Now.AddMinutes(1));
    }

    [Fact]
    public void Check_is_postponed_while_account_is_throttled()
    {
        var account = ActiveAccount();
        account.OnThrottled(Now.AddMinutes(20));
        var monitor = CreateBot();
        var check = monitor.AddTelegramCheck("Status", FiveMinutes, account.Id, StatusScenario(), Now);

        monitor.ScheduleDueChecks(Now, Accounts(account)).ShouldBe(0);

        check.NextRunAt.ShouldBe(Now.AddMinutes(20));
    }

    [Fact]
    public void Exhausted_budget_postpones_check_to_next_window()
    {
        var account = ActiveAccount();
        account.TryReserve(TelegramAccount.MaxMessagesPerHour, Now).ShouldBeNull();
        var monitor = CreateBot();
        var check = monitor.AddTelegramCheck("Status", FiveMinutes, account.Id, StatusScenario(), Now);

        monitor.ScheduleDueChecks(Now.AddMinutes(10), Accounts(account)).ShouldBe(0);
        check.NextRunAt.ShouldBe(Now.AddHours(1));

        monitor.ScheduleDueChecks(Now.AddHours(1), Accounts(account)).ShouldBe(1);
        account.BudgetUsed.ShouldBe(1);
    }

    [Fact]
    public void Late_qr_does_not_overwrite_login_result()
    {
        var account = ActiveAccount();

        account.OnQrIssued("tg://login?token=late", Now);

        account.LoginUrl.ShouldBeNull();
        account.Status.ShouldBe(AccountStatus.Active);
    }

    [Fact]
    public void Relogin_asks_probe_again()
    {
        var account = ActiveAccount();
        account.ClearDomainEvents();

        account.RequestLogin();

        account.Status.ShouldBe(AccountStatus.PendingLogin);
        account.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<AccountLoginRequired>();
    }

    private static Monitor CreateBot() => Monitor.Create("Bot", MonitorKind.TelegramBot, "status_test_bot", Now);

    private static TelegramAccount ActiveAccount()
    {
        var account = TelegramAccount.Create("Probe", Now);
        account.OnLoggedIn(42, "Probe (@probe)");
        return account;
    }

    private static Dictionary<Guid, TelegramAccount> Accounts(TelegramAccount account) => new() { [account.Id] = account };

    private static TelegramScenario StatusScenario() =>
        TelegramScenario.Create([new(StepKind.SendCommand, Text: "/status"), Reply()]);

    private static ScenarioStep Reply(int timeoutSeconds = 30) =>
        new(StepKind.ExpectReply, TimeoutSeconds: timeoutSeconds, Condition: ReplyCondition.Any);
}
