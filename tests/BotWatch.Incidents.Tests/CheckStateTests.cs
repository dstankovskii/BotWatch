using BotWatch.Incidents.Domain;

namespace BotWatch.Incidents.Tests;

public class CheckStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void First_success_moves_unknown_to_up()
    {
        var state = CreateState();

        state.Record(Success(1));

        state.Status.ShouldBe(CheckStatus.Up);
        state.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Single_failure_does_not_open_incident()
    {
        var state = CreateState();
        state.Record(Success(1));

        state.Record(Failure(2));

        state.Status.ShouldBe(CheckStatus.Up);
        state.ConsecutiveFailures.ShouldBe(1);
        state.OpenIncident.ShouldBeNull();
    }

    [Fact]
    public void Two_failures_in_a_row_open_incident_once()
    {
        var state = CreateState();

        state.Record(Failure(1));
        state.Record(Failure(2));
        state.Record(Failure(3));

        state.Status.ShouldBe(CheckStatus.Down);
        state.OpenIncident.ShouldNotBeNull();
        state.OpenIncident.Cause.ShouldBe("timeout");
        state.DomainEvents.OfType<IncidentOpenedEvent>().Count().ShouldBe(1);
    }

    [Fact]
    public void Success_resolves_incident()
    {
        var state = CreateState();
        state.Record(Failure(1));
        state.Record(Failure(2));
        var incident = state.OpenIncident!;

        state.Record(Success(3));

        state.Status.ShouldBe(CheckStatus.Up);
        state.OpenIncident.ShouldBeNull();
        incident.ResolvedAt.ShouldBe(Now.AddMinutes(3));
        var resolved = state.DomainEvents.OfType<IncidentResolvedEvent>().Single();
        resolved.OpenedAt.ShouldBe(Now.AddMinutes(2));
    }

    [Fact]
    public void Late_result_is_ignored()
    {
        var state = CreateState();
        state.Record(Failure(1));
        state.Record(Failure(3));

        state.Record(Success(2));

        state.Status.ShouldBe(CheckStatus.Down);
    }

    [Fact]
    public void Disabling_closes_incident_without_notification()
    {
        var state = CreateState();
        state.Record(Failure(1));
        state.Record(Failure(2));
        var incident = state.OpenIncident!;
        state.ClearDomainEvents();

        state.Configure("Site", "Health", TimeSpan.FromMinutes(1), enabled: false, Now.AddMinutes(3));

        state.Status.ShouldBe(CheckStatus.Unknown);
        incident.ResolvedAt.ShouldNotBeNull();
        state.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Results_of_disabled_check_are_ignored()
    {
        var state = CreateState();
        state.Configure("Site", "Health", TimeSpan.FromMinutes(1), enabled: false, Now);

        state.Record(Failure(1));
        state.Record(Failure(2));

        state.OpenIncident.ShouldBeNull();
        state.Status.ShouldBe(CheckStatus.Unknown);
    }

    [Fact]
    public void Check_without_results_becomes_stale_after_grace_period_once()
    {
        var state = CreateState();
        state.Record(Success(1));

        // интервал минута, но раньше трёх минут тишины не тревожим
        state.MarkStale(Now.AddMinutes(3.5));
        state.Status.ShouldBe(CheckStatus.Up);

        state.MarkStale(Now.AddMinutes(4.5));
        state.MarkStale(Now.AddMinutes(10));

        state.Status.ShouldBe(CheckStatus.Stale);
        var stale = state.DomainEvents.OfType<CheckStaleEvent>().Single();
        stale.LastResultAt.ShouldBe(Now.AddMinutes(1));
    }

    [Fact]
    public void Long_interval_waits_two_and_a_half_intervals()
    {
        var state = CreateState();
        state.Configure("Bot", "Voice", TimeSpan.FromHours(1), enabled: true, Now);

        state.IsOverdue(Now.AddHours(2.4)).ShouldBeFalse();
        state.IsOverdue(Now.AddHours(2.6)).ShouldBeTrue();
    }

    [Fact]
    public void Result_after_silence_resumes_check()
    {
        var state = CreateState();
        state.Record(Success(1));
        state.MarkStale(Now.AddMinutes(5));

        state.Record(Success(6));

        state.Status.ShouldBe(CheckStatus.Up);
        var resumed = state.DomainEvents.OfType<CheckResumedEvent>().Single();
        resumed.StaleSince.ShouldBe(Now.AddMinutes(5));
        state.IsOverdue(Now.AddMinutes(8)).ShouldBeFalse();
    }

    [Fact]
    public void Silence_does_not_touch_open_incident()
    {
        var state = CreateState();
        state.Record(Failure(1));
        state.Record(Failure(2));
        var incident = state.OpenIncident!;

        state.MarkStale(Now.AddMinutes(10));
        state.Record(Failure(11));

        state.Status.ShouldBe(CheckStatus.Down);
        state.OpenIncident.ShouldBe(incident);
        incident.ResolvedAt.ShouldBeNull();
        state.DomainEvents.OfType<IncidentOpenedEvent>().Count().ShouldBe(1);
    }

    [Fact]
    public void Changing_interval_restarts_waiting()
    {
        var state = CreateState();

        state.Configure("Site", "Health", TimeSpan.FromMinutes(2), enabled: true, Now.AddMinutes(10));

        state.IsOverdue(Now.AddMinutes(12)).ShouldBeFalse();
        state.IsOverdue(Now.AddMinutes(15.5)).ShouldBeTrue();
    }

    [Fact]
    public void Disabled_check_is_never_stale()
    {
        var state = CreateState();
        state.Configure("Site", "Health", TimeSpan.FromMinutes(1), enabled: false, Now);

        state.MarkStale(Now.AddDays(1));

        state.Status.ShouldBe(CheckStatus.Unknown);
        state.DomainEvents.ShouldBeEmpty();
    }

    private static CheckState CreateState()
    {
        var state = CheckState.Create(Guid.NewGuid(), Guid.NewGuid(), Now);
        state.Configure("Site", "Health", TimeSpan.FromMinutes(1), enabled: true, Now);
        return state;
    }

    private static CheckOutcome Success(int minute) => new(Guid.NewGuid(), true, null, Now.AddMinutes(minute));

    private static CheckOutcome Failure(int minute) => new(Guid.NewGuid(), false, "timeout", Now.AddMinutes(minute));
}
