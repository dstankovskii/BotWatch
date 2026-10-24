using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace BotWatch.Messaging.Consuming;

internal interface IHandlerInvoker
{
    Task InvokeAsync(
        IServiceProvider services,
        ReadOnlyMemory<byte> body,
        MessageContext context,
        CancellationToken cancellationToken);
}

internal sealed class HandlerInvoker<TMessage, THandler> : IHandlerInvoker
    where TMessage : IIntegrationMessage
    where THandler : class, IMessageHandler<TMessage>
{
    public Task InvokeAsync(
        IServiceProvider services,
        ReadOnlyMemory<byte> body,
        MessageContext context,
        CancellationToken cancellationToken)
    {
        var message = JsonSerializer.Deserialize<TMessage>(body.Span, MessageJson.Options)
            ?? throw new InvalidOperationException($"Empty payload for {typeof(TMessage).Name}");

        var handler = services.GetRequiredService<THandler>();
        return handler.HandleAsync(message, context, cancellationToken);
    }
}
