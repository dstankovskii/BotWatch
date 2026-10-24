using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace BotWatch.Incidents.Background;

public class HeartbeatOptions
{
    public const string SectionName = "Heartbeat";

    // например https://hc-ping.com/<uuid>. пусто - пинг выключен
    public string? Url { get; set; }

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
}

// dead man's switch: внешний сервис ждёт пинг и сам пишет, если тот перестал приходить.
// так узнаём, что упал сам мониторинг: сервер, база или брокер, а не наблюдаемые боты
public sealed class Heartbeat(
    IHttpClientFactory httpClientFactory,
    HealthCheckService healthChecks,
    IOptions<HeartbeatOptions> options,
    TimeProvider timeProvider,
    ILogger<Heartbeat> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.Url))
        {
            logger.LogInformation("Heartbeat:Url is not set, dead man's switch is off");
            return;
        }

        var url = new Uri(settings.Url);

        // первый пинг через интервал, а не сразу: при старте брокер ещё не подключён, и сервис честно не готов
        using var timer = new PeriodicTimer(settings.Interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await BeatAsync(url, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // ошибку только пишем: пропущенный пинг и есть сигнал, ради которого всё затевалось
                logger.LogWarning(ex, "Heartbeat ping failed");
            }
        }
    }

    private async Task BeatAsync(Uri url, CancellationToken cancellationToken)
    {
        // без базы или брокера алерты не уйдут, значит и «живы» говорить нельзя
        var report = await healthChecks.CheckHealthAsync(x => x.Tags.Contains("ready"), cancellationToken);
        if (report.Status != HealthStatus.Healthy)
        {
            logger.LogWarning("Skipping heartbeat, service is {Status}", report.Status);
            return;
        }

        using var response = await httpClientFactory.CreateClient(nameof(Heartbeat)).GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
