using BotWatch.Incidents.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BotWatch.Incidents.Background;

// результаты приходят сами, а их отсутствие нужно искать: проба упала, аккаунт отвалился, планировщик встал
public sealed class StaleDetector(
    IServiceScopeFactory scopeFactory,
    IncidentsMetrics metrics,
    TimeProvider timeProvider,
    ILogger<StaleDetector> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await DetectAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Stale detection failed");
            }
        }
    }

    private async Task DetectAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        List<Guid> overdue;

        // проверок десятки, проще отфильтровать в памяти по тому же правилу, что в домене
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<IncidentsDbContext>();
            var states = await dbContext.CheckStates.AsNoTracking().Where(x => x.Enabled).ToListAsync(cancellationToken);
            overdue = [.. states.Where(x => x.IsOverdue(now)).Select(x => x.CheckId)];

            // заодно обновляем метрику статусов: проверки уже загружены. просроченные будут Stale со следующего прохода
            metrics.ReportStates(states);
        }

        foreach (var checkId in overdue)
        {
            await MarkStaleAsync(checkId, now, cancellationToken);
        }
    }

    // под блокировкой проверяем заново: пока искали, мог прийти результат или другая реплика уже отметила
    private async Task MarkStaleAsync(Guid checkId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IncidentsDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var state = await dbContext.GetStateForUpdateAsync(checkId, cancellationToken);
        if (state is null || !state.IsOverdue(now))
        {
            return;
        }

        state.MarkStale(now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogWarning(
            "Check {CheckId} {Monitor}/{Check} is stale, last result at {LastResultAt}",
            state.CheckId,
            state.MonitorName,
            state.CheckName,
            state.LastResultAt);
    }
}
