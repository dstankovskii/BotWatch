using BotWatch.Domain;

namespace BotWatch.Monitoring.Domain;

public record CheckConfigured(
    Guid CheckId,
    Guid MonitorId,
    string MonitorName,
    string CheckName,
    TimeSpan Interval,
    bool Enabled) : IDomainEvent;

public record CheckDeleted(Guid CheckId, Guid MonitorId) : IDomainEvent;

public record HttpCheckDue(
    Guid JobId,
    Guid CheckId,
    Guid MonitorId,
    string Url,
    HttpCheckSettings Settings,
    DateTimeOffset RequestedAt,
    DateTimeOffset ExpiresAt) : IDomainEvent;
