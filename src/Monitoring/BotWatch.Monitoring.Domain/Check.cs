using BotWatch.Domain;

namespace BotWatch.Monitoring.Domain;

public class Check
{
    public static readonly TimeSpan MinHttpInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MinTelegramInterval = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaxInterval = TimeSpan.FromDays(1);

    public Guid Id { get; private set; }

    public Guid MonitorId { get; private set; }

    public string Name { get; private set; } = null!;

    public TimeSpan Interval { get; private set; }

    public bool Enabled { get; private set; }

    public DateTimeOffset NextRunAt { get; private set; }

    public DateTimeOffset? LastScheduledAt { get; private set; }

    public HttpCheckSettings? Http { get; private set; }

    // для Telegram-проверок: каким аккаунтом и что делаем
    public Guid? AccountId { get; private set; }

    public TelegramScenario? Scenario { get; private set; }

    private Check()
    {
    }

    internal static Check CreateTelegram(
        Guid monitorId,
        string name,
        TimeSpan interval,
        Guid accountId,
        TelegramScenario scenario,
        DateTimeOffset now)
    {
        EnsureTelegramInterval(interval, scenario);

        return new Check
        {
            Id = Guid.CreateVersion7(),
            MonitorId = monitorId,
            Name = name,
            Interval = interval,
            Enabled = true,
            NextRunAt = now,
            AccountId = accountId,
            Scenario = scenario
        };
    }

    internal void UpdateTelegram(
        string name,
        TimeSpan interval,
        Guid accountId,
        TelegramScenario scenario,
        bool enabled,
        DateTimeOffset now)
    {
        EnsureTelegramInterval(interval, scenario);

        Name = name;
        Enabled = enabled;
        AccountId = accountId;
        Scenario = scenario;
        ChangeInterval(interval, now);
    }

    internal static Check CreateHttp(Guid monitorId, string name, TimeSpan interval, HttpCheckSettings settings, DateTimeOffset now)
    {
        EnsureHttpInterval(interval, settings);

        // первый запуск сразу, чтобы статус появился без ожидания интервала
        return new Check
        {
            Id = Guid.CreateVersion7(),
            MonitorId = monitorId,
            Name = name,
            Interval = interval,
            Enabled = true,
            NextRunAt = now,
            Http = settings
        };
    }

    internal void UpdateHttp(string name, TimeSpan interval, HttpCheckSettings settings, bool enabled, DateTimeOffset now)
    {
        EnsureHttpInterval(interval, settings);

        Name = name;
        Enabled = enabled;
        Http = settings;
        ChangeInterval(interval, now);
    }

    public bool IsDueAt(DateTimeOffset now) => Enabled && NextRunAt <= now;

    internal Guid Schedule(DateTimeOffset now)
    {
        LastScheduledAt = now;
        NextRunAt = now + Interval;
        return Guid.CreateVersion7();
    }

    // запустить сейчас нельзя (бюджет аккаунта, FloodWait) - пробуем позже, но не пропускаем
    internal void Postpone(DateTimeOffset until)
    {
        NextRunAt = until;
    }

    private void ChangeInterval(TimeSpan interval, DateTimeOffset now)
    {
        // интервал уменьшили - не ждём старый, длинный
        if (interval < Interval && NextRunAt > now + interval)
        {
            NextRunAt = now + interval;
        }

        Interval = interval;
    }

    private static void EnsureTelegramInterval(TimeSpan interval, TelegramScenario scenario)
    {
        if (interval < MinTelegramInterval || interval > MaxInterval)
        {
            throw new DomainException($"Telegram check interval must be between {MinTelegramInterval} and {MaxInterval}");
        }

        // иначе следующий запуск начнётся, пока предыдущий ещё ждёт ответа
        if (scenario.MaxDuration >= interval)
        {
            throw new DomainException(
                $"Scenario can take up to {scenario.MaxDuration.TotalSeconds} s, which is not shorter than the interval");
        }
    }

    private static void EnsureHttpInterval(TimeSpan interval, HttpCheckSettings settings)
    {
        if (interval < MinHttpInterval || interval > MaxInterval)
        {
            throw new DomainException($"HTTP check interval must be between {MinHttpInterval} and {MaxInterval}");
        }

        if (settings.Timeout >= interval)
        {
            throw new DomainException("Timeout must be shorter than the interval");
        }
    }
}
