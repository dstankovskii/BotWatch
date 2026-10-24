using System.Diagnostics;
using System.Text;
using RabbitMQ.Client;

namespace BotWatch.Messaging;

public sealed class RabbitPublisher(RabbitConnection connection) : IAsyncDisposable
{
    // канал не потокобезопасен для публикации, а одного канала с confirms нам хватает
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        using var activity = MessagingTelemetry.StartPublish(message);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var channel = await GetChannelAsync(cancellationToken);
            var properties = new BasicProperties
            {
                MessageId = message.MessageId.ToString(),
                Type = message.Type,
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Timestamp = new AmqpTimestamp(message.OccurredAt.ToUnixTimeSeconds()),
                Headers = new Dictionary<string, object?>()
            };

            // трассировка выключена - всё равно передаём исходный контекст дальше, он ничего не стоит
            var traceParent = activity?.Id ?? message.TraceParent;
            if (traceParent is not null)
            {
                properties.Headers[MessagingTelemetry.TraceParentHeader] = traceParent;
            }

            // confirms включены - вызов вернётся после ack брокера, nack прилетит исключением
            await channel.BasicPublishAsync(
                message.Exchange,
                message.RoutingKey,
                mandatory: false,
                properties,
                Encoding.UTF8.GetBytes(message.Payload),
                cancellationToken);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        _lock.Dispose();
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        var rabbit = await connection.GetAsync(cancellationToken);
        _channel = await rabbit.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        await RabbitTopology.DeclareExchangesAsync(_channel, cancellationToken);

        return _channel;
    }
}
