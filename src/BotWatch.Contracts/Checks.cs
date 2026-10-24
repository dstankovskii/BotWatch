using BotWatch.Messaging;

namespace BotWatch.Contracts;

// Enabled - итоговый флаг: проверка включена и её монитор тоже
public record CheckChanged(
    Guid CheckId,
    Guid MonitorId,
    string MonitorName,
    string CheckName,
    int IntervalSeconds,
    bool Enabled) : IIntegrationMessage
{
    public static MessageRoute Route { get; } = new(Exchanges.Events, "check.changed");
}

public record CheckRemoved(Guid CheckId, Guid MonitorId) : IIntegrationMessage
{
    public static MessageRoute Route { get; } = new(Exchanges.Events, "check.removed");
}

// JobId - id конкретного запуска проверки, результат приходит с тем же id
public record CheckCompleted(
    Guid JobId,
    Guid CheckId,
    Guid MonitorId,
    bool Success,
    int DurationMs,
    string? Error,
    string Probe,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt) : IIntegrationMessage
{
    public static MessageRoute Route { get; } = new(Exchanges.Events, "check.completed");
}
