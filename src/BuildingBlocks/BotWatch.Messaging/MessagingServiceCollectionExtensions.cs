using BotWatch.Messaging.Consuming;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BotWatch.Messaging;

public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMq(this IServiceCollection services, IConfiguration configuration)
    {
        // повторный вызов зарегистрировал бы health check с тем же именем, а это ошибка при старте
        if (services.Any(x => x.ServiceType == typeof(RabbitConnection)))
        {
            return services;
        }

        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddSingleton<RabbitConnection>();
        services.AddSingleton<RabbitPublisher>();
        services.AddSingleton<RabbitConsumerFactory>();
        services.AddHealthChecks().AddCheck<RabbitHealthCheck>("rabbitmq", tags: ["ready"]);
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
