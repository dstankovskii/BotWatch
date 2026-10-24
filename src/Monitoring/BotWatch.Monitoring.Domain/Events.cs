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

public record TelegramCheckDue(
    Guid JobId,
    Guid CheckId,
    Guid MonitorId,
    Guid AccountId,
    string BotUsername,
    TelegramScenario Scenario,
    DateTimeOffset RequestedAt,
    DateTimeOffset ExpiresAt) : IDomainEvent;

public record AccountLoginRequired(Guid AccountId, string DisplayName) : IDomainEvent;

public record AccountRemoved(Guid AccountId) : IDomainEvent;
