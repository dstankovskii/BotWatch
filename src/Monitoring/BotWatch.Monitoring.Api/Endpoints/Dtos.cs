using BotWatch.Monitoring.Domain;

namespace BotWatch.Monitoring.Endpoints;

public record CreateMonitorRequest(string Name, MonitorKind Kind, string Target);

public record UpdateMonitorRequest(string Name, string Target, bool Enabled);

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

public record MonitorDto(
    Guid Id,
    string Name,
    MonitorKind Kind,
    string Target,
    bool Enabled,
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
            monitor.CreatedAt,
            [.. monitor.Checks.Select(CheckDto.From)]);
    }
}

public record CheckDto(
    Guid Id,
    string Name,
    int IntervalSeconds,
    bool Enabled,
    DateTimeOffset NextRunAt,
    DateTimeOffset? LastScheduledAt,
    HttpCheckDto? Http)
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
            check.Http is null ? null : HttpCheckDto.From(check.Http));
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
