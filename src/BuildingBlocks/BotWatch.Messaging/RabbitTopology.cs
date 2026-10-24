using RabbitMQ.Client;

namespace BotWatch.Messaging;

internal static class RabbitTopology
{
    public static string RetryQueue(string queue) => $"{queue}.retry";

    public static string DeadLetterQueue(string queue) => $"{queue}.dlq";

    public static async Task DeclareExchangesAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            Exchanges.Events,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            Exchanges.Jobs,
            ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);
    }

    public static async Task DeclareQueueAsync(
        IChannel channel,
        string queue,
        IEnumerable<MessageRoute> bindings,
        CancellationToken cancellationToken)
    {
        await DeclareExchangesAsync(channel, cancellationToken);

        await channel.QueueDeclareAsync(
            queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        // retry-очередь без потребителей: сообщение лежит до истечения TTL и возвращается в основную.
        // TTL ставим на сообщение, а не на очередь, чтобы задержку можно было менять без пересоздания очереди
        await channel.QueueDeclareAsync(
            RetryQueue(queue),
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = "",
                ["x-dead-letter-routing-key"] = queue
            },
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            DeadLetterQueue(queue),
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        foreach (var route in bindings.Distinct())
        {
            await channel.QueueBindAsync(queue, route.Exchange, route.RoutingKey, cancellationToken: cancellationToken);
        }
    }
}
