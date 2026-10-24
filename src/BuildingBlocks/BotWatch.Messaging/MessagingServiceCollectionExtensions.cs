using BotWatch.Messaging.Consuming;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BotWatch.Messaging;

public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMq(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddSingleton<RabbitConnection>();
        services.AddSingleton<RabbitPublisher>();
        return services;
    }

    public static IServiceCollection AddRabbitConsumer(
        this IServiceCollection services,
        string queue,
        Action<ConsumerBuilder> configure)
    {
        var definition = new ConsumerBuilder(services, queue);
        configure(definition);

        services.AddSingleton<IHostedService>(provider =>
            ActivatorUtilities.CreateInstance<RabbitConsumerService>(provider, definition));

        return services;
    }
}
