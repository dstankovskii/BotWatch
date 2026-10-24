using BotWatch.Incidents.Domain;
using BotWatch.Incidents.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BotWatch.Incidents.Endpoints;

public static class IncidentEndpoints
{
    private const int MaxResults = 500;

    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/states", GetStatesAsync);
        app.MapGet("/api/checks/{checkId:guid}/results", GetResultsAsync);
        app.MapGet("/api/incidents", GetIncidentsAsync);

        return app;
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

public record IncidentDto(
    Guid Id,
    Guid CheckId,
    Guid MonitorId,
    string? Cause,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ResolvedAt);
