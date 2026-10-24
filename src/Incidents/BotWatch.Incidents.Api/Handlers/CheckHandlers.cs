using BotWatch.Contracts;
using BotWatch.Incidents.Domain;
using BotWatch.Incidents.Infrastructure;
using BotWatch.Messaging.Consuming;
using Microsoft.EntityFrameworkCore;

namespace BotWatch.Incidents.Handlers;

public sealed class CheckChangedHandler(IncidentsDbContext dbContext, TimeProvider timeProvider) : IMessageHandler<CheckChanged>
{
    public async Task HandleAsync(CheckChanged message, MessageContext context, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var state = await dbContext.GetStateForUpdateAsync(message.CheckId, cancellationToken);
        if (state is null)
        {
            state = CheckState.Create(message.CheckId, message.MonitorId, now);
            dbContext.CheckStates.Add(state);
        }

        state.Configure(message.MonitorName, message.CheckName, TimeSpan.FromSeconds(message.IntervalSeconds), message.Enabled, now);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed class CheckRemovedHandler(IncidentsDbContext dbContext, TimeProvider timeProvider) : IMessageHandler<CheckRemoved>
{
    public async Task HandleAsync(CheckRemoved message, MessageContext context, CancellationToken cancellationToken)
    {
        var state = await dbContext.GetStateForUpdateAsync(message.CheckId, cancellationToken);
        if (state is null)
        {
            return;
        }

        state.Remove(timeProvider.GetUtcNow());
        dbContext.CheckStates.Remove(state);

        // история без проверки никому не нужна, инциденты оставляем - по ним видно, что было
        await dbContext.CheckResults.Where(x => x.CheckId == message.CheckId).ExecuteDeleteAsync(cancellationToken);
        await dbContext.DailyUptime.Where(x => x.CheckId == message.CheckId).ExecuteDeleteAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed class CheckCompletedHandler(
    IncidentsDbContext dbContext,
    IncidentsMetrics metrics,
    ILogger<CheckCompletedHandler> logger) : IMessageHandler<CheckCompleted>
{
    public async Task HandleAsync(CheckCompleted message, MessageContext context, CancellationToken cancellationToken)
    {
        var state = await dbContext.GetStateForUpdateAsync(message.CheckId, cancellationToken);
        if (state is null)
        {
            // проверку уже удалили, а результат ещё был в пути
            logger.LogInformation("Result {JobId} for unknown check {CheckId}, skipping", message.JobId, message.CheckId);
            return;
        }

        dbContext.CheckResults.Add(CheckResult.Create(
            message.JobId,
            message.CheckId,
            message.Success,
            message.DurationMs,
            message.Error,
            message.Probe,
            message.StartedAt,
            message.FinishedAt));

        var previousStatus = state.Status;
        state.Record(new CheckOutcome(message.JobId, message.Success, message.Error, message.FinishedAt));

        await dbContext.SaveChangesAsync(cancellationToken);
        metrics.RecordResult(message.Probe, message.Success, message.DurationMs);

        if (state.Status != previousStatus)
        {
            logger.LogInformation(
                "Check {CheckId} {Monitor}/{Check} is {Status} now",
                state.CheckId,
                state.MonitorName,
                state.CheckName,
                state.Status);
        }
    }
}
