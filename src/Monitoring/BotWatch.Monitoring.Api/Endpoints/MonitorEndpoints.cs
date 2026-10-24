using BotWatch.Domain;
using BotWatch.Monitoring.Domain;
using BotWatch.Monitoring.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace BotWatch.Monitoring.Endpoints;

public static class MonitorEndpoints
{
    public static IEndpointRouteBuilder MapMonitorEndpoints(this IEndpointRouteBuilder app)
    {
        var monitors = app.MapGroup("/api/monitors");

        monitors.MapGet("/", GetMonitorsAsync);
        monitors.MapGet("/{id:guid}", GetMonitorAsync);
        monitors.MapPost("/", CreateMonitorAsync);
        monitors.MapPut("/{id:guid}", UpdateMonitorAsync);
        monitors.MapDelete("/{id:guid}", DeleteMonitorAsync);

        monitors.MapPost("/{id:guid}/checks/http", AddHttpCheckAsync);
        monitors.MapPut("/{id:guid}/checks/http/{checkId:guid}", UpdateHttpCheckAsync);
        monitors.MapPost("/{id:guid}/checks/telegram", AddTelegramCheckAsync);
        monitors.MapPut("/{id:guid}/checks/telegram/{checkId:guid}", UpdateTelegramCheckAsync);
        monitors.MapDelete("/{id:guid}/checks/{checkId:guid}", DeleteCheckAsync);

        return app;
    }

    private static async Task<List<MonitorDto>> GetMonitorsAsync(MonitoringDbContext dbContext, CancellationToken cancellationToken)
    {
        var monitors = await dbContext.Monitors
            .AsNoTracking()
            .Include(x => x.Checks)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return [.. monitors.Select(MonitorDto.From)];
    }

    private static async Task<Results<Ok<MonitorDto>, NotFound>> GetMonitorAsync(
        Guid id,
        MonitoringDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var monitor = await dbContext.Monitors
            .AsNoTracking()
            .Include(x => x.Checks)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return monitor is null ? TypedResults.NotFound() : TypedResults.Ok(MonitorDto.From(monitor));
    }

    private static async Task<Created<MonitorDto>> CreateMonitorAsync(
        CreateMonitorRequest request,
        MonitoringDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var monitor = Monitor.Create(request.Name, request.Kind, request.Target, timeProvider.GetUtcNow());

        dbContext.Monitors.Add(monitor);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/monitors/{monitor.Id}", MonitorDto.From(monitor));
    }

    private static Task<Results<Ok<MonitorDto>, NotFound>> UpdateMonitorAsync(
        Guid id,
        UpdateMonitorRequest request,
        MonitoringDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return ChangeMonitorAsync(
            dbContext,
            id,
            monitor => monitor.Update(request.Name, request.Target, request.Enabled),
            cancellationToken);
    }

    private static async Task<Results<NoContent, NotFound>> DeleteMonitorAsync(
        Guid id,
        MonitoringDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var monitor = await dbContext.GetForUpdateAsync(id, cancellationToken);
        if (monitor is null)
        {
            return TypedResults.NotFound();
        }

        monitor.Delete();
        dbContext.Monitors.Remove(monitor);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static Task<Results<Ok<MonitorDto>, NotFound>> AddHttpCheckAsync(
        Guid id,
        HttpCheckRequest request,
        MonitoringDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        return ChangeMonitorAsync(
            dbContext,
            id,
            monitor => monitor.AddHttpCheck(
                request.Name,
                TimeSpan.FromSeconds(request.IntervalSeconds),
                request.ToSettings(),
                timeProvider.GetUtcNow()),
            cancellationToken);
    }

    private static Task<Results<Ok<MonitorDto>, NotFound>> UpdateHttpCheckAsync(
        Guid id,
        Guid checkId,
        HttpCheckRequest request,
        MonitoringDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        return ChangeMonitorAsync(
            dbContext,
            id,
            monitor => monitor.UpdateHttpCheck(
                checkId,
                request.Name,
                TimeSpan.FromSeconds(request.IntervalSeconds),
                request.ToSettings(),
                request.Enabled,
                timeProvider.GetUtcNow()),
            cancellationToken);
    }

    private static Task<Results<Ok<MonitorDto>, NotFound>> AddTelegramCheckAsync(
        Guid id,
        TelegramCheckRequest request,
        MonitoringDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var scenario = TelegramScenario.Create(request.Steps);
        var interval = TimeSpan.FromSeconds(request.IntervalSeconds);

        return ChangeMonitorAsync(
            dbContext,
            id,
            async monitor =>
            {
                await EnsureAccountBudgetAsync(dbContext, request.AccountId, null, scenario, interval, cancellationToken);
                monitor.AddTelegramCheck(request.Name, interval, request.AccountId, scenario, timeProvider.GetUtcNow());
            },
            cancellationToken);
    }

    private static Task<Results<Ok<MonitorDto>, NotFound>> UpdateTelegramCheckAsync(
        Guid id,
        Guid checkId,
        TelegramCheckRequest request,
        MonitoringDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var scenario = TelegramScenario.Create(request.Steps);
        var interval = TimeSpan.FromSeconds(request.IntervalSeconds);

        return ChangeMonitorAsync(
            dbContext,
            id,
            async monitor =>
            {
                if (request.Enabled)
                {
                    await EnsureAccountBudgetAsync(dbContext, request.AccountId, checkId, scenario, interval, cancellationToken);
                }

                monitor.UpdateTelegramCheck(
                    checkId,
                    request.Name,
                    interval,
                    request.AccountId,
                    scenario,
                    request.Enabled,
                    timeProvider.GetUtcNow());
            },
            cancellationToken);
    }

    // бюджет общий на все проверки аккаунта, поэтому считаем по всем мониторам.
    // строку аккаунта блокируем, чтобы два параллельных сохранения не влезли в бюджет вдвоём
    private static async Task EnsureAccountBudgetAsync(
        MonitoringDbContext dbContext,
        Guid accountId,
        Guid? exceptCheckId,
        TelegramScenario scenario,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        if (await dbContext.GetAccountForUpdateAsync(accountId, cancellationToken) is null)
        {
            throw new DomainException($"Telegram account {accountId} not found");
        }

        var others = await dbContext.Checks
            .AsNoTracking()
            .Where(x => x.AccountId == accountId && x.Enabled && x.Id != exceptCheckId)
            .ToListAsync(cancellationToken);

        TelegramAccount.EnsureFitsBudget(others
            .Select(x => (x.Scenario!.MessagesPerRun, x.Interval))
            .Append((scenario.MessagesPerRun, interval)));
    }

    private static Task<Results<Ok<MonitorDto>, NotFound>> DeleteCheckAsync(
        Guid id,
        Guid checkId,
        MonitoringDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return ChangeMonitorAsync(dbContext, id, monitor => monitor.RemoveCheck(checkId), cancellationToken);
    }

    private static Task<Results<Ok<MonitorDto>, NotFound>> ChangeMonitorAsync(
        MonitoringDbContext dbContext,
        Guid id,
        Action<Monitor> change,
        CancellationToken cancellationToken)
    {
        return ChangeMonitorAsync(
            dbContext,
            id,
            monitor =>
            {
                change(monitor);
                return Task.CompletedTask;
            },
            cancellationToken);
    }

    private static async Task<Results<Ok<MonitorDto>, NotFound>> ChangeMonitorAsync(
        MonitoringDbContext dbContext,
        Guid id,
        Func<Monitor, Task> change,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var monitor = await dbContext.GetForUpdateAsync(id, cancellationToken);
        if (monitor is null)
        {
            return TypedResults.NotFound();
        }

        await change(monitor);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Ok(MonitorDto.From(monitor));
    }
}
