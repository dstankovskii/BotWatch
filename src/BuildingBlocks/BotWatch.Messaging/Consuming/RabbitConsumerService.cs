using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BotWatch.Messaging.Consuming;

// читает одну очередь на своём канале всё время жизни процесса
internal sealed class RabbitConsumerService(
    ConsumerBuilder definition,
    RabbitConnection connection,
    IServiceScopeFactory scopeFactory,
    ILogger<RabbitConsumerService> logger) : BackgroundService
{
    private const string RetryCountHeader = "x-retry-count";
    private const string ErrorHeader = "x-error";
    private const int MaxErrorLength = 1000;

    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var rabbit = await connection.GetAsync(stoppingToken);
        _channel = await rabbit.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: false,
                publisherConfirmationTrackingEnabled: false,
                consumerDispatchConcurrency: definition.Concurrency),
            stoppingToken);

        await RabbitTopology.DeclareQueueAsync(_channel, definition.Queue, definition.Bindings, stoppingToken);
        await _channel.BasicQosAsync(0, definition.PrefetchCount, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, delivery) => OnReceivedAsync(_channel, delivery, stoppingToken);
        await _channel.BasicConsumeAsync(definition.Queue, autoAck: false, consumer, stoppingToken);

        logger.LogInformation("Consuming {Queue}", definition.Queue);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        // неподтверждённые сообщения брокер вернёт в очередь сам, когда канал закроется
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }
    }

    private async Task OnReceivedAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        var context = CreateContext(delivery);
        try
        {
            if (!definition.Handlers.TryGetValue(context.Type, out var invoker))
            {
                // ретраи тут не помогут, сразу в DLQ
                await MoveToDeadLetterAsync(channel, delivery, context, $"No handler for message type '{context.Type}'");
                return;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            await InvokeAsync(scope.ServiceProvider, invoker, delivery.Body, context, stoppingToken);

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            await RetryOrDeadLetterAsync(channel, delivery, context, ex);
        }
        catch (OperationCanceledException)
        {
            // остановка посреди обработки: сообщение не подтверждено и вернётся в очередь при закрытии канала
        }
    }

    private Task InvokeAsync(
        IServiceProvider services,
        IHandlerInvoker invoker,
        ReadOnlyMemory<byte> body,
        MessageContext context,
        CancellationToken cancellationToken)
    {
        Func<Task> next = () => invoker.InvokeAsync(services, body, context, cancellationToken);

        // первый зарегистрированный фильтр - самый внешний
        foreach (var filterType in Enumerable.Reverse(definition.Filters))
        {
            var filter = (IConsumeFilter)services.GetRequiredService(filterType);
            var inner = next;
            next = () => filter.InvokeAsync(context, inner, cancellationToken);
        }

        return next();
    }

    private async Task RetryOrDeadLetterAsync(
        IChannel channel,
        BasicDeliverEventArgs delivery,
        MessageContext context,
        Exception exception)
    {
        if (context.RetryCount >= definition.MaxRetries)
        {
            logger.LogError(
                exception,
                "Message {MessageId} {Type} from {Queue} failed after {RetryCount} retries, moving to DLQ",
                context.MessageId,
                context.Type,
                context.Queue,
                context.RetryCount);

            await MoveToDeadLetterAsync(channel, delivery, context, exception.Message);
            return;
        }

        logger.LogWarning(
            exception,
            "Message {MessageId} {Type} from {Queue} failed, retry {Retry} of {MaxRetries}",
            context.MessageId,
            context.Type,
            context.Queue,
            context.RetryCount + 1,
            definition.MaxRetries);

        var properties = CopyProperties(delivery);
        properties.Headers![RetryCountHeader] = context.RetryCount + 1;
        properties.Expiration = ((long)definition.RetryDelay.TotalMilliseconds).ToString();

        await RepublishAsync(channel, delivery, RabbitTopology.RetryQueue(definition.Queue), properties);
    }

    private async Task MoveToDeadLetterAsync(
        IChannel channel,
        BasicDeliverEventArgs delivery,
        MessageContext context,
        string error)
    {
        var properties = CopyProperties(delivery);
        properties.Headers![ErrorHeader] = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;

        logger.LogWarning("Message {MessageId} {Type} moved to DLQ: {Error}", context.MessageId, context.Type, error);

        await RepublishAsync(channel, delivery, RabbitTopology.DeadLetterQueue(definition.Queue), properties);
    }

    private async Task RepublishAsync(IChannel channel, BasicDeliverEventArgs delivery, string queue, BasicProperties properties)
    {
        try
        {
            await channel.BasicPublishAsync("", queue, mandatory: false, properties, delivery.Body);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
        }
        catch (Exception ex) when (channel.IsOpen)
        {
            // не смогли переложить - пусть брокер отдаст сообщение заново, лучше дубль, чем потеря
            logger.LogError(ex, "Failed to move message to {Queue}, requeueing", queue);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true);
        }
    }

    private MessageContext CreateContext(BasicDeliverEventArgs delivery)
    {
        var properties = delivery.BasicProperties;
        Guid.TryParse(properties.MessageId, out var messageId);

        var retryCount = properties.Headers?.TryGetValue(RetryCountHeader, out var value) == true && value is not null
            ? Convert.ToInt32(value)
            : 0;

        return new MessageContext(
            messageId,
            properties.Type ?? "",
            DateTimeOffset.FromUnixTimeSeconds(properties.Timestamp.UnixTime),
            definition.Queue,
            retryCount);
    }

    private static BasicProperties CopyProperties(BasicDeliverEventArgs delivery)
    {
        var source = delivery.BasicProperties;

        return new BasicProperties
        {
            MessageId = source.MessageId,
            Type = source.Type,
            ContentType = source.ContentType,
            DeliveryMode = DeliveryModes.Persistent,
            Timestamp = source.Timestamp,
            Headers = source.Headers is null ? [] : new Dictionary<string, object?>(source.Headers)
        };
    }
}
