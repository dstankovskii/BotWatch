using System.Diagnostics;
using System.Text;

namespace BotWatch.Messaging;

// трасса идёт через брокер в заголовке traceparent (W3C). через outbox тоже: контекст
// запоминается при создании сообщения, а публикация из релея позже продолжает ту же трассу
public static class MessagingTelemetry
{
    public const string SourceName = "BotWatch.Messaging";
    public const string TraceParentHeader = "traceparent";

    public static readonly ActivitySource Source = new(SourceName);

    public static string? CurrentTraceParent()
    {
        var activity = Activity.Current;
        return activity is { IdFormat: ActivityIdFormat.W3C } ? activity.Id : null;
    }

    internal static Activity? StartPublish(OutgoingMessage message)
    {
        ActivityContext.TryParse(message.TraceParent, null, out var parent);

        var activity = Source.StartActivity($"{message.RoutingKey} publish", ActivityKind.Producer, parent);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.operation.type", "publish");
        activity?.SetTag("messaging.destination.name", message.Exchange);
        activity?.SetTag("messaging.rabbitmq.destination.routing_key", message.RoutingKey);
        activity?.SetTag("messaging.message.id", message.MessageId.ToString());
        return activity;
    }

    internal static Activity? StartProcess(IDictionary<string, object?>? headers, string queue, string type, Guid messageId)
    {
        var traceParent = headers?.TryGetValue(TraceParentHeader, out var value) == true
            ? value switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                string text => text,
                _ => null
            }
            : null;

        ActivityContext.TryParse(traceParent, null, out var parent);

        var activity = Source.StartActivity($"{queue} process", ActivityKind.Consumer, parent);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.operation.type", "process");
        activity?.SetTag("messaging.destination.name", queue);
        activity?.SetTag("messaging.message.type", type);
        activity?.SetTag("messaging.message.id", messageId.ToString());
        return activity;
    }
}
