using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BotWatch.Messaging.Consuming;

// потребители, которые появляются и исчезают во время работы, например очередь на Telegram-аккаунт
public sealed class RabbitConsumerFactory(
    RabbitConnection connection,
    IServiceScopeFactory scopeFactory,
    ILoggerFactory loggerFactory)
{
    public async Task<RabbitConsumer> StartAsync(
        string queue,
        Action<ConsumerBuilder> configure,
        CancellationToken cancellationToken)
    {
        var definition = new ConsumerBuilder(null, queue);
        configure(definition);

        var consumer = new RabbitConsumer(definition, scopeFactory, loggerFactory.CreateLogger<RabbitConsumer>());
        try
        {
            await consumer.StartAsync(await connection.GetAsync(cancellationToken), cancellationToken);
            return consumer;
        }
        catch
        {
            await consumer.DisposeAsync();
            throw;
        }
    }

    // удаляет очередь вместе с её retry и DLQ
    public async Task DeleteQueueAsync(string queue, CancellationToken cancellationToken)
    {
        var rabbit = await connection.GetAsync(cancellationToken);
        await using var channel = await rabbit.CreateChannelAsync(cancellationToken: cancellationToken);

        foreach (var name in new[] { queue, RabbitTopology.RetryQueue(queue), RabbitTopology.DeadLetterQueue(queue) })
        {
            await channel.QueueDeleteAsync(name, ifUnused: false, ifEmpty: false, cancellationToken: cancellationToken);
        }
    }
}
