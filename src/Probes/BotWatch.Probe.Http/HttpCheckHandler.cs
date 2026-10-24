using System.Diagnostics;
using System.Text;
using BotWatch.Contracts;
using BotWatch.Messaging;
using BotWatch.Messaging.Consuming;

namespace BotWatch.Probe.Http;

public sealed class HttpCheckHandler(
    IHttpClientFactory httpClientFactory,
    RabbitPublisher publisher,
    TimeProvider timeProvider,
    ILogger<HttpCheckHandler> logger) : IMessageHandler<HttpCheckRequested>
{
    public const string ClientName = "checks";
    public const string ProbeName = "http";

    // тело больше этого не читаем: для проверки подстроки хватает, а память не раздуваем
    private const int MaxBodyBytes = 1024 * 1024;

    public async Task HandleAsync(HttpCheckRequested job, MessageContext context, CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();
        if (startedAt > job.ExpiresAt)
        {
            // очередь стояла дольше интервала, уже запланирован следующий запуск
            logger.LogWarning("Job {JobId} for check {CheckId} expired at {ExpiresAt}, skipping", job.JobId, job.CheckId, job.ExpiresAt);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var error = await RunAsync(job, cancellationToken);
        stopwatch.Stop();

        var result = new CheckCompleted(
            job.JobId,
            job.CheckId,
            job.MonitorId,
            Success: error is null,
            DurationMs: (int)stopwatch.ElapsedMilliseconds,
            error,
            ProbeName,
            startedAt,
            timeProvider.GetUtcNow());

        // MessageId = JobId: если опубликуем дважды (ретрай после падения), incidents отсечёт дубль через inbox
        await publisher.PublishAsync(
            OutgoingMessage.Create(result, result.FinishedAt, messageId: job.JobId),
            cancellationToken);

        logger.LogInformation(
            "Check {CheckId} {Url}: {Result} in {DurationMs} ms",
            job.CheckId,
            job.Url,
            error ?? "OK",
            result.DurationMs);
    }

    private async Task<string?> RunAsync(HttpCheckRequested job, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(job.TimeoutSeconds));

        try
        {
            using var request = new HttpRequestMessage(new HttpMethod(job.Method), job.Url);
            using var response = await httpClientFactory.CreateClient(ClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            if ((int)response.StatusCode != job.ExpectedStatus)
            {
                return $"Expected status {job.ExpectedStatus}, got {(int)response.StatusCode}";
            }

            if (job.BodyContains is null)
            {
                return null;
            }

            var body = await ReadBodyAsync(response, timeout.Token);
            return body.Contains(job.BodyContains, StringComparison.Ordinal)
                ? null
                : $"Response body does not contain '{job.BodyContains}'";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return $"Timeout after {job.TimeoutSeconds} s";
        }
        catch (HttpRequestException ex)
        {
            return ex.HttpRequestError == HttpRequestError.Unknown ? ex.Message : $"{ex.HttpRequestError}: {ex.Message}";
        }
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        var buffer = new byte[MaxBodyBytes];
        var total = 0;
        int read;
        while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken)) > 0)
        {
            total += read;
        }

        return Encoding.UTF8.GetString(buffer, 0, total);
    }
}
