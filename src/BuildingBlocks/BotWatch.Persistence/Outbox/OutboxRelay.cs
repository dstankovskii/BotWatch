using BotWatch.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BotWatch.Persistence.Outbox;

internal sealed class OutboxRelay<TDbContext>(
    IServiceScopeFactory scopeFactory,
    RabbitPublisher publisher,
    TimeProvider timeProvider,
    ILogger<OutboxRelay<TDbContext>> logger) : BackgroundService where TDbContext : BotWatchDbContext
{
    private const int BatchSize = 50;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private DateTimeOffset _nextCleanupAt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval, timeProvider);
        do
        {
            try
            {
                // пока выгребаем полные пачки - таймер не ждём
                while (await PublishBatchAsync(stoppingToken) == BatchSize)
                {
                }

                await CleanupIfNeededAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Outbox relay failed");
                await Task.Delay(ErrorDelay, timeProvider, stoppingToken);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<int> PublishBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // skip locked - несколько реплик сервиса не опубликуют одно и то же
        var messages = await dbContext.OutboxMessages
            .FromSql($"""
                select * from outbox_messages
                where processed_at is null
                order by position
                limit {BatchSize}
                for update skip locked
                """)
            .ToListAsync(cancellationToken);

        var published = 0;
        foreach (var message in messages)
        {
            try
            {
                await publisher.PublishAsync(message.ToOutgoing(), cancellationToken);
                message.MarkProcessed(timeProvider.GetUtcNow());
                published++;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                message.MarkFailed(ex.Message);
                logger.LogWarning(ex, "Failed to publish outbox message {MessageId} {Type}", message.Id, message.Type);

                // порядок важен - остаток пачки отправим на следующем тике
                break;
            }
        }

        // если упадём между публикацией и коммитом - сообщение уйдёт повторно, дубль отсечёт inbox получателя
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return published;
    }

    private async Task CleanupIfNeededAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (now < _nextCleanupAt)
        {
            return;
        }

        _nextCleanupAt = now + CleanupInterval;

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var threshold = now - Retention;

        var outbox = await dbContext.OutboxMessages
            .Where(x => x.ProcessedAt < threshold)
            .ExecuteDeleteAsync(cancellationToken);

        var inbox = await dbContext.InboxMessages
            .Where(x => x.ProcessedAt < threshold)
            .ExecuteDeleteAsync(cancellationToken);

        if (outbox + inbox > 0)
        {
            logger.LogInformation("Removed {Outbox} outbox and {Inbox} inbox messages older than {Threshold}", outbox, inbox, threshold);
        }
    }
}
