using BotWatch.Messaging.Consuming;
using BotWatch.Persistence.Inbox;
using BotWatch.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace BotWatch.Persistence;

public static class PersistenceExtensions
{
    public static IServiceCollection AddBotWatchDbContext<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName) where TDbContext : BotWatchDbContext
    {
        var connectionString = configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{connectionStringName}' is not configured");

        services.AddDbContext<TDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());

        services.AddHealthChecks().AddDbContextCheck<TDbContext>(tags: ["ready"]);
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }

    public static IServiceCollection AddOutboxRelay<TDbContext>(this IServiceCollection services)
        where TDbContext : BotWatchDbContext
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<OutboxRelay<TDbContext>>();
        return services;
    }

    public static IServiceCollection AddSecretProtector(this IServiceCollection services, IConfiguration configuration)
    {
        // ключ проверяем при старте, а не при первом алерте
        var protector = new SecretProtector(configuration[SecretProtector.KeyConfigPath]);
        services.AddSingleton(protector);
        return services;
    }

    public static ConsumerBuilder UseInbox<TDbContext>(this ConsumerBuilder builder) where TDbContext : BotWatchDbContext
    {
        return builder.UseFilter<InboxFilter<TDbContext>>();
    }

    public static async Task MigrateDatabaseAsync<TDbContext>(this IHost host) where TDbContext : DbContext
    {
        // EF берёт блокировку на время миграции, поэтому несколько реплик при старте друг другу не мешают
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TDbContext>().Database.MigrateAsync();
    }
}
