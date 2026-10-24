using System.Text.Json;
using System.Text.Json.Serialization;

namespace BotWatch.Messaging;

public record OutgoingMessage(
    Guid MessageId,
    string Type,
    string Payload,
    string Exchange,
    string RoutingKey,
    DateTimeOffset OccurredAt)
{
    public static OutgoingMessage Create<TMessage>(
        TMessage message,
        DateTimeOffset occurredAt,
        Guid? messageId = null) where TMessage : IIntegrationMessage
    {
        return new OutgoingMessage(
            messageId ?? Guid.CreateVersion7(),
            typeof(TMessage).Name,
            JsonSerializer.Serialize(message, MessageJson.Options),
            TMessage.Route.Exchange,
            TMessage.Route.RoutingKey,
            occurredAt);
    }
}

public static class MessageJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
