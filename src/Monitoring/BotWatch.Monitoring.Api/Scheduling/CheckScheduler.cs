using BotWatch.Monitoring.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BotWatch.Monitoring.Scheduling;

internal sealed class CheckScheduler(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<CheckScheduler> logger) : BackgroundService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                while (await ScheduleBatchAsync(stoppingToken) == BatchSize)
                {
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Check scheduling failed");
            }
        }
    }

    private async Task<int> ScheduleBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MonitoringDbContext>();
        var now = timeProvider.GetUtcNow();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // skip locked - реплики планировщика разбирают разные мониторы, монитор под правкой в API пропускаем до следующего тика
        var monitors = await dbContext.Monitors
            .FromSql($"""
                select m.* from monitors m
                where m.enabled
                  and exists (select 1 from checks c where c.monitor_id = m.id and c.enabled and c.next_run_at <= {now})
                limit {BatchSize}
                for update of m skip locked
                """)
            .ToListAsync(cancellationToken);

        if (monitors.Count == 0)
        {
            return 0;
        }

        var ids = monitors.Select(x => x.Id).ToList();
        await dbContext.Checks.Where(x => ids.Contains(x.MonitorId)).LoadAsync(cancellationToken);

        var jobs = monitors.Sum(monitor => monitor.ScheduleDueChecks(now));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogDebug("Scheduled {Jobs} checks for {Monitors} monitors", jobs, monitors.Count);

        return monitors.Count;
    }
}
