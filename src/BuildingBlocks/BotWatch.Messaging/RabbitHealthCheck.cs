using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BotWatch.Messaging;

// соединение поднимают потребители при старте, а дальше его восстанавливает сам клиент.
// пока оно не открыто, сервис не получает сообщений, значит и готовым считаться не должен
internal sealed class RabbitHealthCheck(RabbitConnection connection) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(connection.IsOpen
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("RabbitMQ connection is not open"));
    }
}
