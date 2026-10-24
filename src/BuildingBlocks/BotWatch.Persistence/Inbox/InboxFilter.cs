using BotWatch.Messaging.Consuming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BotWatch.Persistence.Inbox;

public sealed class InboxFilter<TDbContext>(
    TDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<InboxFilter<TDbContext>> logger) : IConsumeFilter where TDbContext : BotWatchDbContext
{
    public async Task InvokeAsync(MessageContext context, Func<Task> next, CancellationToken cancellationToken)
    {
        // отметка в inbox и изменения обработчика в одной транзакции:
        // упал обработчик - откатится и отметка, сообщение придёт снова
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var inserted = await dbContext.Database.ExecuteSqlAsync($"""
            insert into inbox_messages (message_id, consumer, processed_at)
            values ({context.MessageId}, {context.Queue}, {timeProvider.GetUtcNow()})
            on conflict do nothing
            """,
            cancellationToken);

        if (inserted == 0)
        {
            logger.LogInformation(
                "Message {MessageId} {Type} already processed by {Queue}, skipping",
                context.MessageId,
                context.Type,
                context.Queue);
            return;
        }

        await next();
        await transaction.CommitAsync(cancellationToken);
    }
}
