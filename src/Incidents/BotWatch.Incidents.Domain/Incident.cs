namespace BotWatch.Incidents.Domain;

public class Incident
{
    public Guid Id { get; private set; }

    public Guid CheckId { get; private set; }

    public Guid MonitorId { get; private set; }

    // первая ошибка, из-за которой открыли инцидент
    public string? Cause { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    private Incident()
    {
    }

    internal static Incident Open(Guid checkId, Guid monitorId, string? cause, DateTimeOffset now)
    {
        return new Incident
        {
            Id = Guid.CreateVersion7(),
            CheckId = checkId,
            MonitorId = monitorId,
            Cause = cause,
            OpenedAt = now
        };
    }

    internal void Resolve(DateTimeOffset now) => ResolvedAt = now;
}
