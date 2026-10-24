using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BotWatch.Messaging.Consuming;

// services == null, когда потребитель создаётся на лету через RabbitConsumerFactory:
// контейнер уже собран, обработчики создаются через ActivatorUtilities
public sealed class ConsumerBuilder(IServiceCollection? services, string queue)
{
    public string Queue { get; } = queue;

    public ushort PrefetchCount { get; set; } = 16;

    // сколько сообщений обрабатываем параллельно. по умолчанию по одному, чтобы сохранялся порядок
    public ushort Concurrency { get; set; } = 1;

    public int MaxRetries { get; set; } = 5;

    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(10);

    internal Dictionary<string, IHandlerInvoker> Handlers { get; } = [];

    internal List<MessageRoute> Bindings { get; } = [];

    internal List<Type> Filters { get; } = [];

    public ConsumerBuilder Handle<TMessage, THandler>(string? routingKey = null)
        where TMessage : IIntegrationMessage
        where THandler : class, IMessageHandler<TMessage>
    {
        services?.TryAddScoped<THandler>();
        Handlers[typeof(TMessage).Name] = new HandlerInvoker<TMessage, THandler>();
        Bindings.Add(routingKey is null ? TMessage.Route : TMessage.Route with { RoutingKey = routingKey });
        return this;
    }

    public ConsumerBuilder UseFilter<TFilter>() where TFilter : class, IConsumeFilter
    {
        services?.TryAddScoped<TFilter>();
        Filters.Add(typeof(TFilter));
        return this;
    }
}
