using BotWatch.Incidents.Domain;
using BotWatch.Incidents.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BotWatch.Incidents.Endpoints;

public static class IncidentEndpoints
{
    private const int MaxResults = 500;
    private const int MaxUptimeDays = 90;

    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/states", GetStatesAsync);
        app.MapGet("/api/checks/{checkId:guid}/results", GetResultsAsync);
        app.MapGet("/api/incidents", GetIncidentsAsync);
        app.MapGet("/api/uptime", GetUptimeAsync);

        return app;
    }

    // по дням в UTC: для полосок на статус-странице и графиков в админке.
    // свёрнутые дни берём из daily_uptime, остальные (сегодня и ещё не свёрнутые) считаем по сырым результатам.
    // колонки в snake_case: naming convention EF действует и на SqlQuery
    internal static Task<List<UptimeDayDto>> GetUptimeAsync(
        IncidentsDbContext dbContext,
        TimeProvider timeProvider,
        int days = 30,
        CancellationToken cancellationToken = default)
    {
        var since = timeProvider.GetUtcNow().UtcDateTime.Date.AddDays(1 - Math.Clamp(days, 1, MaxUptimeDays));
        var sinceDay = DateOnly.FromDateTime(since);

        return dbContext.Database
            .SqlQuery<UptimeDayDto>($"""
                WITH live AS (
                    SELECT check_id,
                           (finished_at AT TIME ZONE 'UTC')::date AS day,
                           count(*) AS total,
                           count(*) FILTER (WHERE NOT success) AS failed,
                           avg(duration_ms)::int AS avg_duration_ms
                    FROM check_results
                    WHERE finished_at >= {new DateTimeOffset(since, TimeSpan.Zero)}
                    GROUP BY 1, 2
                )
                SELECT check_id, day, total::bigint AS total, failed::bigint AS failed, avg_duration_ms
                FROM daily_uptime
                WHERE day >= {sinceDay}
                UNION ALL
                SELECT live.check_id, live.day, live.total, live.failed, live.avg_duration_ms
                FROM live
                WHERE NOT EXISTS (SELECT 1 FROM daily_uptime d WHERE d.check_id = live.check_id AND d.day = live.day)
                ORDER BY 1, 2
                """)
            .ToListAsync(cancellationToken);
    }

    private static Task<List<CheckStateDto>> GetStatesAsync(IncidentsDbContext dbContext, CancellationToken cancellationToken)
    {
        return dbContext.CheckStates
            .AsNoTracking()
            .OrderBy(x => x.MonitorName)
            .ThenBy(x => x.CheckName)
            .Select(x => new CheckStateDto(
                x.CheckId,
                x.MonitorId,
                x.MonitorName,
                x.CheckName,
                x.Enabled,
                x.Status,
                x.StatusChangedAt,
                x.LastResultAt,
                x.LastError,
                x.OpenIncidentId))
            .ToListAsync(cancellationToken);
    }

    private static Task<List<CheckResultDto>> GetResultsAsync(
        Guid checkId,
        IncidentsDbContext dbContext,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return dbContext.CheckResults
            .AsNoTracking()
            .Where(x => x.CheckId == checkId)
            .OrderByDescending(x => x.FinishedAt)
            .Take(Math.Clamp(limit, 1, MaxResults))
            .Select(x => new CheckResultDto(x.Id, x.Success, x.DurationMs, x.Error, x.Probe, x.StartedAt, x.FinishedAt))
            .ToListAsync(cancellationToken);
    }

    private static Task<List<IncidentDto>> GetIncidentsAsync(
        IncidentsDbContext dbContext,
        bool openOnly = false,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Incidents.AsNoTracking();
        if (openOnly)
        {
            query = query.Where(x => x.ResolvedAt == null);
        }

        return query
            .OrderByDescending(x => x.OpenedAt)
            .Take(Math.Clamp(limit, 1, MaxResults))
            .Select(x => new IncidentDto(x.Id, x.CheckId, x.MonitorId, x.Cause, x.OpenedAt, x.ResolvedAt))
            .ToListAsync(cancellationToken);
    }
}

public record CheckStateDto(
    Guid CheckId,
    Guid MonitorId,
    string MonitorName,
    string CheckName,
    bool Enabled,
    CheckStatus Status,
    DateTimeOffset StatusChangedAt,
    DateTimeOffset? LastResultAt,
    string? LastError,
    Guid? OpenIncidentId);

public record CheckResultDto(
    Guid JobId,
    bool Success,
    int DurationMs,
    string? Error,
    string Probe,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt);

public record UptimeDayDto(Guid CheckId, DateOnly Day, long Total, long Failed, int AvgDurationMs);

public record IncidentDto(
    Guid Id,
    Guid CheckId,
    Guid MonitorId,
    string? Cause,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ResolvedAt);
