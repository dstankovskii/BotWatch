using BotWatch.Messaging;

namespace BotWatch.Contracts;

public record IncidentOpened(
    Guid IncidentId,
    Guid CheckId,
    Guid MonitorId,
    string MonitorName,
    string CheckName,
    string? Cause,
    DateTimeOffset OpenedAt) : IIntegrationMessage
{
    public static MessageRoute Route { get; } = new(Exchanges.Events, "incident.opened");
}

public record CheckStale(
    Guid CheckId,
    Guid MonitorId,
    string MonitorName,
    string CheckName,
    DateTimeOffset? LastResultAt,
    DateTimeOffset DetectedAt) : IIntegrationMessage
{
    public static MessageRoute Route { get; } = new(Exchanges.Events, "incident.stale");
}

public record CheckResumed(
    Guid CheckId,
    Guid MonitorId,
    string MonitorName,
    string CheckName,
    DateTimeOffset StaleSince,
    DateTimeOffset ResumedAt) : IIntegrationMessage
{
    public static MessageRoute Route { get; } = new(Exchanges.Events, "incident.resumed");
}

public record IncidentResolved(
    Guid IncidentId,
    Guid CheckId,
    Guid MonitorId,
    string MonitorName,
    string CheckName,
    DateTimeOffset OpenedAt,
    DateTimeOffset ResolvedAt) : IIntegrationMessage
{
    public static MessageRoute Route { get; } = new(Exchanges.Events, "incident.resolved");
}
