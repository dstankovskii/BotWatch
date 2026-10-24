using BotWatch.Monitoring.Domain;

namespace BotWatch.Monitoring.Endpoints;

public record CreateMonitorRequest(string Name, MonitorKind Kind, string Target);

public record UpdateMonitorRequest(string Name, string Target, bool Enabled, bool IsPublic);

public record HttpCheckRequest(
    string Name,
    int IntervalSeconds,
    string? Path,
    string? Method,
    int ExpectedStatus,
    string? BodyContains,
    int TimeoutSeconds,
    bool Enabled = true)
{
    public HttpCheckSettings ToSettings()
    {
        return HttpCheckSettings.Create(Path, Method, ExpectedStatus, BodyContains, TimeSpan.FromSeconds(TimeoutSeconds));
    }
}

// Voice в шаге - ogg в base64
public record TelegramCheckRequest(
    string Name,
    int IntervalSeconds,
    Guid AccountId,
    IReadOnlyList<ScenarioStep> Steps,
    bool Enabled = true);

public record CreateAccountRequest(string DisplayName);

public record MonitorDto(
    Guid Id,
    string Name,
    MonitorKind Kind,
    string Target,
    bool Enabled,
    bool IsPublic,
    DateTimeOffset CreatedAt,
    IReadOnlyList<CheckDto> Checks)
{
    public static MonitorDto From(Monitor monitor)
    {
        return new MonitorDto(
            monitor.Id,
            monitor.Name,
            monitor.Kind,
            monitor.Target,
            monitor.Enabled,
            monitor.IsPublic,
            monitor.CreatedAt,
            [.. monitor.Checks.Select(CheckDto.From)]);
    }
}

// для статус-страницы: только то, что можно показать без логина
public record PublicMonitorDto(Guid Id, string Name, IReadOnlyList<Guid> CheckIds);

public record CheckDto(
    Guid Id,
    string Name,
    int IntervalSeconds,
    bool Enabled,
    DateTimeOffset NextRunAt,
    DateTimeOffset? LastScheduledAt,
    HttpCheckDto? Http,
    TelegramCheckDto? Telegram)
{
    public static CheckDto From(Check check)
    {
        return new CheckDto(
            check.Id,
            check.Name,
            (int)check.Interval.TotalSeconds,
            check.Enabled,
            check.NextRunAt,
            check.LastScheduledAt,
            check.Http is null ? null : HttpCheckDto.From(check.Http),
            check.Scenario is null ? null : TelegramCheckDto.From(check.AccountId!.Value, check.Scenario));
    }
}

public record TelegramCheckDto(Guid AccountId, int MessagesPerRun, IReadOnlyList<StepDto> Steps)
{
    public static TelegramCheckDto From(Guid accountId, TelegramScenario scenario)
    {
        return new TelegramCheckDto(accountId, scenario.MessagesPerRun, [.. scenario.Steps.Select(StepDto.From)]);
    }
}

// сам файл голосового в списке не отдаём, только размер: он до мегабайта
public record StepDto(
    StepKind Kind,
    string? Text,
    IReadOnlyList<string>? Phrases,
    int? VoiceBytes,
    int? TimeoutSeconds,
    ReplyCondition? Condition)
{
    public static StepDto From(ScenarioStep step)
    {
        return new StepDto(step.Kind, step.Text, step.Phrases, step.Voice?.Length, step.TimeoutSeconds, step.Condition);
    }
}

public record AccountDto(
    Guid Id,
    string DisplayName,
    AccountStatus Status,
    string? TelegramName,
    string? LastError,
    DateTimeOffset? ThrottledUntil,
    bool QrReady,
    int ChecksCount,
    double MessagesPerHour,
    int MessagesPerHourLimit,
    DateTimeOffset CreatedAt)
{
    public static AccountDto From(TelegramAccount account, IReadOnlyCollection<Check> checks)
    {
        return new AccountDto(
            account.Id,
            account.DisplayName,
            account.Status,
            account.TelegramName,
            account.LastError,
            account.ThrottledUntil,
            account.LoginUrl is not null,
            checks.Count,
            Math.Round(TelegramAccount.HourlyLoad(checks
                .Where(x => x.Enabled)
                .Select(x => (x.Scenario!.MessagesPerRun, x.Interval))), 1),
            TelegramAccount.MaxMessagesPerHour,
            account.CreatedAt);
    }
}

public record HttpCheckDto(string Path, string Method, int ExpectedStatus, string? BodyContains, int TimeoutSeconds)
{
    public static HttpCheckDto From(HttpCheckSettings settings)
    {
        return new HttpCheckDto(
            settings.Path,
            settings.Method,
            settings.ExpectedStatus,
            settings.BodyContains,
            (int)settings.Timeout.TotalSeconds);
    }
}
