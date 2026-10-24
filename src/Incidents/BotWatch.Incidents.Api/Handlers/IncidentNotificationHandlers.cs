using BotWatch.Contracts;
using BotWatch.Incidents.Domain;
using BotWatch.Incidents.Infrastructure;
using BotWatch.Incidents.Notifications;
using BotWatch.Messaging.Consuming;
using Microsoft.EntityFrameworkCore;

namespace BotWatch.Incidents.Handlers;

public sealed class IncidentOpenedHandler(
    IncidentsDbContext dbContext,
    TelegramNotifier notifier,
    ILogger<IncidentOpenedHandler> logger) : IMessageHandler<IncidentOpened>
{
    public Task HandleAsync(IncidentOpened message, MessageContext context, CancellationToken cancellationToken)
    {
        return dbContext.NotifyAllAsync(notifier, AlertMessages.Opened(message), logger, cancellationToken);
    }
}

public sealed class IncidentResolvedHandler(
    IncidentsDbContext dbContext,
    TelegramNotifier notifier,
    ILogger<IncidentResolvedHandler> logger) : IMessageHandler<IncidentResolved>
{
    public Task HandleAsync(IncidentResolved message, MessageContext context, CancellationToken cancellationToken)
    {
        return dbContext.NotifyAllAsync(notifier, AlertMessages.Resolved(message), logger, cancellationToken);
    }
}

public sealed class CheckStaleHandler(
    IncidentsDbContext dbContext,
    TelegramNotifier notifier,
    ILogger<CheckStaleHandler> logger) : IMessageHandler<CheckStale>
{
    public Task HandleAsync(CheckStale message, MessageContext context, CancellationToken cancellationToken)
    {
        return dbContext.NotifyAllAsync(notifier, AlertMessages.Stale(message), logger, cancellationToken);
    }
}

public sealed class CheckResumedHandler(
    IncidentsDbContext dbContext,
    TelegramNotifier notifier,
    ILogger<CheckResumedHandler> logger) : IMessageHandler<CheckResumed>
{
    public Task HandleAsync(CheckResumed message, MessageContext context, CancellationToken cancellationToken)
    {
        return dbContext.NotifyAllAsync(notifier, AlertMessages.Resumed(message), logger, cancellationToken);
    }
}

internal static class NotificationExtensions
{
    public static async Task NotifyAllAsync(
        this IncidentsDbContext dbContext,
        TelegramNotifier notifier,
        string html,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var channels = await dbContext.NotificationChannels
            .AsNoTracking()
            .Where(x => x.Enabled)
            .ToListAsync(cancellationToken);

        if (channels.Count == 0)
        {
            logger.LogWarning("No notification channels configured, alert is not sent");
            return;
        }

        var failed = new List<Exception>();
        foreach (var channel in channels)
        {
            try
            {
                await notifier.SendAsync(channel, html, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                failed.Add(ex);
            }
        }

        // при ретрае успешные каналы получат алерт второй раз. дубль лучше, чем потерянный алерт
        if (failed.Count > 0)
        {
            throw new AggregateException("Failed to send alert", failed);
        }
    }
}
