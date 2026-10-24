using BotWatch.Messaging;

namespace BotWatch.Contracts;

// ExpiresAt - после этого времени задание неактуально (уже запланирован следующий запуск), проба его выкидывает
public record HttpCheckRequested(
    Guid JobId,
    Guid CheckId,
    Guid MonitorId,
    string Url,
    string Method,
    int ExpectedStatus,
    string? BodyContains,
    int TimeoutSeconds,
    DateTimeOffset RequestedAt,
    DateTimeOffset ExpiresAt) : IIntegrationMessage
{
    public static MessageRoute Route { get; } = new(Exchanges.Jobs, "http");
}
