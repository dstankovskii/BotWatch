using BotWatch.Domain;

namespace BotWatch.Incidents.Domain;

public enum CheckStatus
{
    Unknown,
    Up,
    Down,

    // результатов нет слишком долго: проба молчит, аккаунт отвалился или планировщик стоит
    Stale
}

public record CheckOutcome(Guid JobId, bool Success, string? Error, DateTimeOffset FinishedAt);

// проекция проверки из monitoring + её текущее состояние
public class CheckState : AggregateRoot
{
    public const int FailuresToOpenIncident = 2;
    public const double StaleAfterIntervals = 2.5;
    private const int MaxErrorLength = 1000;

    // у частых HTTP-проверок 2.5 интервала - это минута с небольшим, такую паузу даёт и обычная очередь
    public static readonly TimeSpan MinStaleAfter = TimeSpan.FromMinutes(3);

    public Guid CheckId { get; private set; }

    public Guid MonitorId { get; private set; }

    public string MonitorName { get; private set; } = null!;

    public string CheckName { get; private set; } = null!;

    public TimeSpan Interval { get; private set; }

    public bool Enabled { get; private set; }

    public CheckStatus Status { get; private set; }

    public int ConsecutiveFailures { get; private set; }

    public DateTimeOffset? LastResultAt { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset StatusChangedAt { get; private set; }

    // с какого момента ждём результаты: включение проверки или смена интервала
    public DateTimeOffset WatchedSince { get; private set; }

    public Guid? OpenIncidentId { get; private set; }

    public Incident? OpenIncident { get; private set; }

    private CheckState()
    {
    }

    public static CheckState Create(Guid checkId, Guid monitorId, DateTimeOffset now)
    {
        return new CheckState
        {
            CheckId = checkId,
            MonitorId = monitorId,
            MonitorName = "",
            CheckName = "",
            Status = CheckStatus.Unknown,
            StatusChangedAt = now,
            WatchedSince = now
        };
    }

    public void Configure(string monitorName, string checkName, TimeSpan interval, bool enabled, DateTimeOffset now)
    {
        MonitorName = monitorName;
        CheckName = checkName;

        // планировщик пересчитает время запуска под новый интервал, старый срок ожидания уже не годится
        if (Interval != interval)
        {
            Interval = interval;
            WatchedSince = now;
        }

        if (Enabled == enabled)
        {
            return;
        }

        Enabled = enabled;
        WatchedSince = now;
        if (!enabled)
        {
            // выключенная проверка не должна висеть в инцидентах, а после включения начинаем с чистого листа
            CloseIncident(now);
            ChangeStatus(CheckStatus.Unknown, now);
            ConsecutiveFailures = 0;
        }
    }

    // проверку удалили в monitoring - открытый инцидент закрываем молча
    public void Remove(DateTimeOffset now)
    {
        CloseIncident(now);
    }

    public void Record(CheckOutcome outcome)
    {
        // результаты могут прийти не по порядку (ретраи, несколько проб), состояние двигаем только вперёд
        if (!Enabled || LastResultAt >= outcome.FinishedAt)
        {
            return;
        }

        LastResultAt = outcome.FinishedAt;

        if (Status == CheckStatus.Stale)
        {
            Resume(outcome.FinishedAt);
        }

        if (outcome.Success)
        {
            RecordSuccess(outcome.FinishedAt);
        }
        else
        {
            RecordFailure(outcome.Error, outcome.FinishedAt);
        }
    }

    public bool IsOverdue(DateTimeOffset now)
    {
        if (!Enabled || Status == CheckStatus.Stale || Interval <= TimeSpan.Zero)
        {
            return false;
        }

        var waitingSince = LastResultAt > WatchedSince ? LastResultAt.Value : WatchedSince;
        var staleAfter = Interval * StaleAfterIntervals;

        return now - waitingSince > (staleAfter > MinStaleAfter ? staleAfter : MinStaleAfter);
    }

    // инцидент не открываем и не закрываем: про сам бот мы ничего не знаем, сломалось наблюдение за ним
    public void MarkStale(DateTimeOffset now)
    {
        if (!IsOverdue(now))
        {
            return;
        }

        ChangeStatus(CheckStatus.Stale, now);
        Raise(new CheckStaleEvent(CheckId, MonitorId, MonitorName, CheckName, LastResultAt, now));
    }

    private void Resume(DateTimeOffset now)
    {
        Raise(new CheckResumedEvent(CheckId, MonitorId, MonitorName, CheckName, StatusChangedAt, now));

        // статус, каким он был до тишины, дальше его поправит сам результат
        ChangeStatus(OpenIncident is not null ? CheckStatus.Down : ConsecutiveFailures == 0 ? CheckStatus.Up : CheckStatus.Unknown, now);
    }

    private void RecordSuccess(DateTimeOffset now)
    {
        ConsecutiveFailures = 0;
        LastError = null;

        if (OpenIncident is not null)
        {
            var incident = OpenIncident;
            incident.Resolve(now);
            OpenIncident = null;
            OpenIncidentId = null;
            Raise(new IncidentResolvedEvent(incident.Id, CheckId, MonitorId, MonitorName, CheckName, incident.OpenedAt, now));
        }

        ChangeStatus(CheckStatus.Up, now);
    }

    private void RecordFailure(string? error, DateTimeOffset now)
    {
        ConsecutiveFailures++;
        LastError = error is { Length: > MaxErrorLength } ? error[..MaxErrorLength] : error;

        // одиночный сбой сети не повод будить человека
        if (ConsecutiveFailures < FailuresToOpenIncident)
        {
            return;
        }

        if (OpenIncident is null && Enabled)
        {
            OpenIncident = Incident.Open(CheckId, MonitorId, LastError, now);
            OpenIncidentId = OpenIncident.Id;
            Raise(new IncidentOpenedEvent(OpenIncident.Id, CheckId, MonitorId, MonitorName, CheckName, LastError, now));
        }

        ChangeStatus(CheckStatus.Down, now);
    }

    private void CloseIncident(DateTimeOffset now)
    {
        if (OpenIncident is null)
        {
            return;
        }

        OpenIncident.Resolve(now);
        OpenIncident = null;
        OpenIncidentId = null;
    }

    private void ChangeStatus(CheckStatus status, DateTimeOffset now)
    {
        if (Status == status)
        {
            return;
        }

        Status = status;
        StatusChangedAt = now;
    }
}
