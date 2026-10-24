namespace BotWatch.Messaging.Consuming;

public interface IMessageHandler<in TMessage> where TMessage : IIntegrationMessage
{
    Task HandleAsync(TMessage message, MessageContext context, CancellationToken cancellationToken);
}

public record MessageContext(
    Guid MessageId,
    string Type,
    DateTimeOffset OccurredAt,
    string Queue,
    int RetryCount);

// обёртка вокруг обработчика: inbox, транзакции и т.п.
public interface IConsumeFilter
{
    Task InvokeAsync(MessageContext context, Func<Task> next, CancellationToken cancellationToken);
}
