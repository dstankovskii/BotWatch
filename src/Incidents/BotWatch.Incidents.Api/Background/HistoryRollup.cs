using BotWatch.Incidents.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BotWatch.Incidents.Background;

public class HistoryOptions
{
    public const string SectionName = "History";

    public int ResultRetentionDays { get; set; } = 30;

    public int UptimeRetentionDays { get; set; } = 400;

    // в Kubernetes выключается: задачу запускает CronJob командой `rollup-history`
    public bool RunInBackground { get; set; } = true;
}

// сворачивает результаты в дневной uptime и удаляет старые. идемпотентна: можно запускать
// сколько угодно раз и из нескольких реплик, день просто пересчитается заново
public sealed class HistoryRollup(
    IncidentsDbContext dbContext,
    IOptions<HistoryOptions> options,
    TimeProvider timeProvider,
    ILogger<HistoryRollup> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var today = timeProvider.GetUtcNow().UtcDateTime.Date;
        var resultsCutoff = new DateTimeOffset(today.AddDays(-settings.ResultRetentionDays), TimeSpan.Zero);
        var uptimeCutoff = DateOnly.FromDateTime(today.AddDays(-settings.UptimeRetentionDays));

        // пересчитываем с последнего свёрнутого дня: поздний результат за него ещё мог прийти, а более старые
        // уже посчитаны полностью. его сырые данные целы - удаление идёт только по дням до прошлой границы.
        // первый запуск сворачивает всё, что есть. сегодняшний день не трогаем - он не закончился
        var lastRolled = await dbContext.DailyUptime.MaxAsync(x => (DateOnly?)x.Day, cancellationToken);
        var from = lastRolled is { } day
            ? new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : DateTimeOffset.UnixEpoch;
        var to = new DateTimeOffset(today, TimeSpan.Zero);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var rolled = await dbContext.Database.ExecuteSqlAsync($"""
            INSERT INTO daily_uptime (check_id, day, total, failed, avg_duration_ms)
            SELECT check_id,
                   (finished_at AT TIME ZONE 'UTC')::date,
                   count(*),
                   count(*) FILTER (WHERE NOT success),
                   avg(duration_ms)::int
            FROM check_results
            WHERE finished_at >= {from} AND finished_at < {to}
            GROUP BY 1, 2
            ON CONFLICT (check_id, day) DO UPDATE
            SET total = excluded.total, failed = excluded.failed, avg_duration_ms = excluded.avg_duration_ms
            """, cancellationToken);

        // граница по полуночи: удаляем только целые дни, которые уже свёрнуты
        var results = await dbContext.CheckResults
            .Where(x => x.FinishedAt < resultsCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        var days = await dbContext.DailyUptime
            .Where(x => x.Day < uptimeCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "History rollup: {Rolled} check-days since {From:yyyy-MM-dd}, removed {Results} results and {Days} old days",
            rolled,
            from,
            results,
            days);
    }
}

// в Compose задача живёт внутри сервиса и срабатывает раз в час
public sealed class HistoryRollupService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<HistoryRollupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<HistoryRollup>().RunAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "History rollup failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
