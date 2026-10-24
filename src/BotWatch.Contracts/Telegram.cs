using BotWatch.Messaging;

namespace BotWatch.Contracts;

public enum ScenarioStepKind
{
    SendText,
    SendCommand,
    SendRandom,
    SendVoice,
    ClickButton,
    ExpectReply
}

public enum ReplyCondition
{
    Any,
    Contains,
    Regex,
    HasButton,
    IsVoice,
    HasFile
}

// один плоский шаг вместо иерархии: проще сериализовать и хранить. какие поля заполнены, зависит от Kind
public record ScenarioStep(
    ScenarioStepKind Kind,
    string? Text = null,
    IReadOnlyList<string>? Phrases = null,
    byte[]? Voice = null,
    int? TimeoutSeconds = null,
    ReplyCondition? Condition = null);

// уходит в очередь конкретного аккаунта, routing key - RoutingKeyFor(AccountId)
public record TelegramCheckRequested(
    Guid JobId,
    Guid CheckId,
    Guid MonitorId,
    Guid AccountId,
    string BotUsername,
    IReadOnlyList<ScenarioStep> Steps,
    DateTimeOffset RequestedAt,
    DateTimeOffset ExpiresAt) : IIntegrationMessage
{
    public static MessageRoute Route { get; } = new(Exchanges.Jobs, "tg.account");

    public static string RoutingKeyFor(Guid accountId) => $"tg.account.{accountId}";
}

// повторный запрос для существующего аккаунта - перелогин: старая сессия выкидывается
public record AccountLoginRequested(Guid AccountId, string DisplayName) : IIntegrationMessage
{
    public static MessageRoute Route { get; } = new(Exchanges.Jobs, "tg.control");
}

public record AccountRemoveRequested(Guid AccountId) : IIntegrationMessage
{
    public static MessageRoute Route { get; } = new(Exchanges.Jobs, "tg.control");
}

// QR обновляется примерно раз в 30 секунд, каждый раз приходит новое событие
public record AccountQrIssued(Guid AccountId, string LoginUrl, DateTimeOffset IssuedAt) : IIntegrationMessage
{
    public static MessageRoute Route { get; } = new(Exchanges.Events, "account.qr");
}

public record AccountLoggedIn(Guid AccountId, long TelegramUserId, string TelegramName) : IIntegrationMessage
{
    public static MessageRoute Route { get; } = new(Exchanges.Events, "account.logged-in");
}

public record AccountLoginFailed(Guid AccountId, string Reason) : IIntegrationMessage
{
    public static MessageRoute Route { get; } = new(Exchanges.Events, "account.login-failed");
}

public record AccountThrottled(Guid AccountId, DateTimeOffset Until) : IIntegrationMessage
{
    public static MessageRoute Route { get; } = new(Exchanges.Events, "account.throttled");
}

// сессию отозвали или аккаунт забанили, нужен новый вход
public record AccountRevoked(Guid AccountId, string Reason) : IIntegrationMessage
{
    public static MessageRoute Route { get; } = new(Exchanges.Events, "account.revoked");
}
