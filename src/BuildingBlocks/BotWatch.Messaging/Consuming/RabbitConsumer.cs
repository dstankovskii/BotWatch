using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BotWatch.Messaging.Consuming;

// читает одну очередь на своём канале. живёт либо весь процесс (RabbitConsumerService),
// либо пока его не остановят (очередь аккаунта в tg-probe живёт, пока у пробы есть Lease)
public sealed class RabbitConsumer : IAsyncDisposable
{
    private const string RetryCountHeader = "x-retry-count";
    private const string ErrorHeader = "x-error";
    private const int MaxErrorLength = 1000;

    // сколько ждём при остановке, пока обработчики допишут ack
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(10);

    private readonly ConsumerBuilder _definition;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stopping = new();

    private IChannel _channel = null!;
    private string? _consumerTag;
    private int _inFlight;

    internal RabbitConsumer(ConsumerBuilder definition, IServiceScopeFactory scopeFactory, ILogger logger)
    {
        _definition = definition;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public string Queue => _definition.Queue;

    internal async Task StartAsync(IConnection connection, CancellationToken cancellationToken)
    {
        _channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: false,
                publisherConfirmationTrackingEnabled: false,
                consumerDispatchConcurrency: _definition.Concurrency),
            cancellationToken);

        await RabbitTopology.DeclareQueueAsync(_channel, _definition.Queue, _definition.Bindings, cancellationToken);
        await _channel.BasicQosAsync(0, _definition.PrefetchCount, global: false, cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, delivery) => OnReceivedAsync(delivery, _stopping.Token);
        _consumerTag = await _channel.BasicConsumeAsync(_definition.Queue, autoAck: false, consumer, cancellationToken);

        _logger.LogInformation("Consuming {Queue}", _definition.Queue);
    }

    public async ValueTask DisposeAsync()
    {
        // сначала просим брокер больше не слать, потом прерываем обработчики и ждём их ack.
        // закрыть канал раньше - и уже обработанное сообщение вернётся в очередь вторым разом
        if (_consumerTag is not null && _channel.IsOpen)
        {
            try
            {
                await _channel.BasicCancelAsync(_consumerTag);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cancel consumer on {Queue}", _definition.Queue);
            }
        }

        await _stopping.CancelAsync();
        await DrainAsync();

        // неподтверждённые сообщения брокер вернёт в очередь сам, когда канал закроется
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        _stopping.Dispose();
        _logger.LogInformation("Stopped consuming {Queue}", _definition.Queue);
    }

    private async Task DrainAsync()
    {
        var deadline = DateTime.UtcNow + DrainTimeout;
        while (Volatile.Read(ref _inFlight) > 0)
        {
            if (DateTime.UtcNow > deadline)
            {
                _logger.LogWarning("{Queue}: handlers did not finish in {Timeout}, closing anyway", _definition.Queue, DrainTimeout);
                return;
            }

            await Task.Delay(50);
        }
    }

    private async Task OnReceivedAsync(BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        Interlocked.Increment(ref _inFlight);
        var context = CreateContext(delivery);
        try
        {
            if (!_definition.Handlers.TryGetValue(context.Type, out var invoker))
            {
                // ретраи тут не помогут, сразу в DLQ
                await MoveToDeadLetterAsync(delivery, context, $"No handler for message type '{context.Type}'");
                return;
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            await InvokeAsync(scope.ServiceProvider, invoker, delivery.Body, context, stoppingToken);

            // работа уже сделана, поэтому ack не отменяем даже при остановке
            await _channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            await RetryOrDeadLetterAsync(delivery, context, ex);
        }
        catch (OperationCanceledException)
        {
            // остановка посреди обработки: сообщение не подтверждено и вернётся в очередь при закрытии канала
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
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
        foreach (var filterType in Enumerable.Reverse(_definition.Filters))
        {
            var filter = (IConsumeFilter)ActivatorUtilities.GetServiceOrCreateInstance(services, filterType);
            var inner = next;
            next = () => filter.InvokeAsync(context, inner, cancellationToken);
        }

        return next();
    }

    private async Task RetryOrDeadLetterAsync(
        BasicDeliverEventArgs delivery,
        MessageContext context,
        Exception exception)
    {
        if (context.RetryCount >= _definition.MaxRetries)
        {
            _logger.LogError(
                exception,
                "Message {MessageId} {Type} from {Queue} failed after {RetryCount} retries, moving to DLQ",
                context.MessageId,
                context.Type,
                context.Queue,
                context.RetryCount);

            await MoveToDeadLetterAsync(delivery, context, exception.Message);
            return;
        }

        _logger.LogWarning(
            exception,
            "Message {MessageId} {Type} from {Queue} failed, retry {Retry} of {MaxRetries}",
            context.MessageId,
            context.Type,
            context.Queue,
            context.RetryCount + 1,
            _definition.MaxRetries);

        var properties = CopyProperties(delivery);
        properties.Headers![RetryCountHeader] = context.RetryCount + 1;
        properties.Expiration = ((long)_definition.RetryDelay.TotalMilliseconds).ToString();

        await RepublishAsync(delivery, RabbitTopology.RetryQueue(_definition.Queue), properties);
    }

    private async Task MoveToDeadLetterAsync(BasicDeliverEventArgs delivery, MessageContext context, string error)
    {
        var properties = CopyProperties(delivery);
        properties.Headers![ErrorHeader] = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;

        _logger.LogWarning("Message {MessageId} {Type} moved to DLQ: {Error}", context.MessageId, context.Type, error);

        await RepublishAsync(delivery, RabbitTopology.DeadLetterQueue(_definition.Queue), properties);
    }

    private async Task RepublishAsync(BasicDeliverEventArgs delivery, string queue, BasicProperties properties)
    {
        try
        {
            await _channel.BasicPublishAsync("", queue, mandatory: false, properties, delivery.Body);
            await _channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
        }
        catch (Exception ex) when (_channel.IsOpen)
        {
            // не смогли переложить - пусть брокер отдаст сообщение заново, лучше дубль, чем потеря
            _logger.LogError(ex, "Failed to move message to {Queue}, requeueing", queue);
            await _channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true);
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
            _definition.Queue,
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
