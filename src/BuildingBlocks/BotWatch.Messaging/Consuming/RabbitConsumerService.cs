using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BotWatch.Messaging.Consuming;

// потребитель на всё время жизни процесса
internal sealed class RabbitConsumerService(
    ConsumerBuilder definition,
    RabbitConnection connection,
    IServiceScopeFactory scopeFactory,
    ILogger<RabbitConsumer> logger) : BackgroundService
{
    private RabbitConsumer? _consumer;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _consumer = new RabbitConsumer(definition, scopeFactory, logger);
        await _consumer.StartAsync(await connection.GetAsync(stoppingToken), stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        if (_consumer is not null)
        {
            await _consumer.DisposeAsync();
        }
    }
}
