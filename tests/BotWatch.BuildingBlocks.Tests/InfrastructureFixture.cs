using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace BotWatch.BuildingBlocks.Tests;

public sealed class InfrastructureFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-alpine").Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public Uri RabbitUri => new(_rabbit.GetConnectionString());

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());

        using var host = TestHost.Build(this, queue: "init", configure: _ => { });
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _rabbit.DisposeAsync().AsTask());
    }
}

[CollectionDefinition(nameof(InfrastructureCollection))]
public class InfrastructureCollection : ICollectionFixture<InfrastructureFixture>;
