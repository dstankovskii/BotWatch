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

public record IncidentResolvedEvent(
    Guid IncidentId,
    Guid CheckId,
    Guid MonitorId,
    string MonitorName,
    string CheckName,
    DateTimeOffset OpenedAt,
    DateTimeOffset ResolvedAt) : IDomainEvent;
