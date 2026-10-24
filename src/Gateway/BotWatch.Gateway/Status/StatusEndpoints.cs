using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Caching.Memory;

namespace BotWatch.Gateway.Status;

public enum StatusLevel
{
    Unknown,
    Up,
    Down
}

// единственное, что видно без логина. собираем на шлюзе, чтобы наружу не торчали
// внутренние API: имена проверок, ошибки и id аккаунтов публике не нужны
public static class StatusEndpoints
{
    public const string StatusRateLimit = "status";
    private const int Days = 90;
    private const string CacheKey = "status-page";

    // аноним может долбить страницу сколько угодно, сервисы при этом дёргаем не чаще раза в 30 секунд
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    public static IEndpointRouteBuilder MapStatusEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/status", GetStatusAsync)
            .AllowAnonymous()
            .RequireRateLimiting(StatusRateLimit);

        return app;
    }

    private static async Task<Results<Ok<StatusPageDto>, ProblemHttpResult>> GetStatusAsync(
        ServiceClient services,
        IMemoryCache cache,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out StatusPageDto? cached))
        {
            return TypedResults.Ok(cached!);
        }

        StatusPageDto page;
        try
        {
            page = await BuildAsync(services, timeProvider, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return TypedResults.Problem("Status is temporarily unavailable", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        cache.Set(CacheKey, page, CacheFor);
        return TypedResults.Ok(page);
    }

    private static async Task<StatusPageDto> BuildAsync(
        ServiceClient services,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var monitorsTask = services.GetPublicMonitorsAsync(cancellationToken);
        var statesTask = services.GetStatesAsync(cancellationToken);
        var uptimeTask = services.GetUptimeAsync(Days, cancellationToken);
        await Task.WhenAll(monitorsTask, statesTask, uptimeTask);

        var states = statesTask.Result.ToDictionary(x => x.CheckId);
        var uptime = uptimeTask.Result.ToLookup(x => x.CheckId);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var monitors = monitorsTask.Result
            .Select(monitor => BuildMonitor(monitor, states, uptime, today))
            .ToList();

        var overall = monitors.Any(x => x.Status == StatusLevel.Down) ? StatusLevel.Down
            : monitors.Count > 0 && monitors.All(x => x.Status == StatusLevel.Up) ? StatusLevel.Up
            : StatusLevel.Unknown;

        return new StatusPageDto(overall, timeProvider.GetUtcNow(), monitors);
    }

    private static StatusMonitorDto BuildMonitor(
        PublicMonitor monitor,
        Dictionary<Guid, CheckState> states,
        ILookup<Guid, UptimeDay> uptime,
        DateOnly today)
    {
        var checkStates = monitor.CheckIds
            .Select(id => states.GetValueOrDefault(id))
            .OfType<CheckState>()
            .ToList();

        var status = checkStates.Any(x => x.Level == StatusLevel.Down) ? StatusLevel.Down
            : checkStates.Count > 0 && checkStates.All(x => x.Level == StatusLevel.Up) ? StatusLevel.Up
            : StatusLevel.Unknown;

        // день монитора = сумма по всем его проверкам
        var byDay = monitor.CheckIds
            .SelectMany(id => uptime[id])
            .GroupBy(x => x.Day)
            .ToDictionary(x => x.Key, x => (Total: x.Sum(d => d.Total), Failed: x.Sum(d => d.Failed)));

        var days = Enumerable.Range(0, Days)
            .Select(i => today.AddDays(i - Days + 1))
            .Select(day => byDay.TryGetValue(day, out var d) && d.Total > 0
                ? new StatusDayDto(day, Percent(d.Total - d.Failed, d.Total))
                : new StatusDayDto(day, null))
            .ToList();

        var total = byDay.Values.Sum(x => x.Total);
        var failed = byDay.Values.Sum(x => x.Failed);

        return new StatusMonitorDto(monitor.Id, monitor.Name, status, total > 0 ? Percent(total - failed, total) : null, days);
    }

    private static double Percent(long good, long total)
    {
        return Math.Round(100.0 * good / total, 2);
    }
}

public record StatusPageDto(StatusLevel Status, DateTimeOffset UpdatedAt, IReadOnlyList<StatusMonitorDto> Monitors);

// Uptime = null - данных за период нет
public record StatusMonitorDto(
    Guid Id,
    string Name,
    StatusLevel Status,
    double? Uptime,
    IReadOnlyList<StatusDayDto> Days);

public record StatusDayDto(DateOnly Day, double? Uptime);
