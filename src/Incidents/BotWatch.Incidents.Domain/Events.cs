using BotWatch.Domain;

namespace BotWatch.Incidents.Domain;

public record IncidentOpenedEvent(
    Guid IncidentId,
    Guid CheckId,
    Guid MonitorId,
    string MonitorName,
    string CheckName,
    string? Cause,
    DateTimeOffset OpenedAt) : IDomainEvent;

public record CheckStaleEvent(
    Guid CheckId,
    Guid MonitorId,
    string MonitorName,
    string CheckName,
    DateTimeOffset? LastResultAt,
    DateTimeOffset DetectedAt) : IDomainEvent;

public record CheckResumedEvent(
    Guid CheckId,
    Guid MonitorId,
    string MonitorName,
    string CheckName,
    DateTimeOffset StaleSince,
    DateTimeOffset ResumedAt) : IDomainEvent;

public record IncidentResolvedEvent(
    Guid IncidentId,
    Guid CheckId,
    Guid MonitorId,
    string MonitorName,
    string CheckName,
    DateTimeOffset OpenedAt,
    DateTimeOffset ResolvedAt) : IDomainEvent;
