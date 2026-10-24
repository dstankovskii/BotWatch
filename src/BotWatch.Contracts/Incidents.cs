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
