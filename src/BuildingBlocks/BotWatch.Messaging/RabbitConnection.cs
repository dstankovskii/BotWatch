using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace BotWatch.Messaging;

public sealed class RabbitConnection(
    IOptions<RabbitMqOptions> options,
    IHostEnvironment environment,
    ILogger<RabbitConnection> logger) : IAsyncDisposable
{
    private static readonly TimeSpan ConnectRetryDelay = TimeSpan.FromSeconds(3);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    public async Task<IConnection> GetAsync(CancellationToken cancellationToken)
    {
        if (_connection is not null)
        {
            return _connection;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // дальше переподключением занимается сам клиент (AutomaticRecoveryEnabled),
            // здесь только первый коннект, когда брокер ещё не поднялся
            while (_connection is null)
            {
                try
                {
                    _connection = await CreateFactory().CreateConnectionAsync(cancellationToken);
                }
                catch (BrokerUnreachableException ex)
                {
                    logger.LogWarning(ex, "RabbitMQ is unreachable, retrying in {Delay}", ConnectRetryDelay);
                    await Task.Delay(ConnectRetryDelay, cancellationToken);
                }
            }

            return _connection;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _lock.Dispose();
    }

    private ConnectionFactory CreateFactory()
    {
        var settings = options.Value;

        return new ConnectionFactory
        {
            HostName = settings.Host,
            Port = settings.Port,
            UserName = settings.Username,
            Password = settings.Password,
            VirtualHost = settings.VirtualHost,
            ClientProvidedName = $"{environment.ApplicationName}@{Environment.MachineName}",
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true
        };
    }
}
