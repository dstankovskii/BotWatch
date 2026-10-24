using BotWatch.Domain;

namespace BotWatch.Monitoring.Domain;

public class Check
{
    public static readonly TimeSpan MinHttpInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaxInterval = TimeSpan.FromDays(1);

    public Guid Id { get; private set; }

    public Guid MonitorId { get; private set; }

    public string Name { get; private set; } = null!;

    public TimeSpan Interval { get; private set; }

    public bool Enabled { get; private set; }

    public DateTimeOffset NextRunAt { get; private set; }

    public DateTimeOffset? LastScheduledAt { get; private set; }

    public HttpCheckSettings? Http { get; private set; }

    private Check()
    {
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

    private void ChangeInterval(TimeSpan interval, DateTimeOffset now)
    {
        // интервал уменьшили - не ждём старый, длинный
        if (interval < Interval && NextRunAt > now + interval)
        {
            NextRunAt = now + interval;
        }

        Interval = interval;
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
