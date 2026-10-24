using System.Diagnostics;
using BotWatch.Contracts;
using BotWatch.Messaging;
using BotWatch.Messaging.Consuming;
using BotWatch.Probe.Telegram.Accounts;
using BotWatch.Probe.Telegram.Telegram;
using TL;

namespace BotWatch.Probe.Telegram.Scenarios;

internal sealed class TelegramCheckHandler(
    AccountRegistry registry,
    RabbitPublisher publisher,
    TimeProvider timeProvider,
    ILogger<TelegramCheckHandler> logger) : IMessageHandler<TelegramCheckRequested>
{
    public const string ProbeName = "telegram";

    public async Task HandleAsync(TelegramCheckRequested job, MessageContext context, CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();
        if (startedAt > job.ExpiresAt)
        {
            logger.LogWarning("Job {JobId} for check {CheckId} expired at {ExpiresAt}, skipping", job.JobId, job.CheckId, job.ExpiresAt);
            return;
        }

        // очередь аккаунта читает только его воркер, так что без воркера сюда не попасть
        if (!registry.TryGet(job.AccountId, out var worker))
        {
            throw new InvalidOperationException($"Worker for account {job.AccountId} is not running");
        }

        if (worker.ThrottledUntil > startedAt)
        {
            // monitoring тоже знает про паузу, но задание могло уйти раньше, чем он узнал
            logger.LogInformation("Account {AccountId} is throttled until {Until}, skipping job {JobId}", job.AccountId, worker.ThrottledUntil, job.JobId);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        string? error;
        try
        {
            await using var chat = await worker.OpenChatAsync(job.BotUsername, cancellationToken);
            error = await ScenarioRunner.RunAsync(chat, job.Steps, cancellationToken);
        }
        catch (RpcException ex) when (TelegramErrors.IsFloodWait(ex))
        {
            // проблема аккаунта, а не бота: результат не публикуем, чтобы не открыть ложный инцидент
            await worker.OnThrottledAsync(ex.X, cancellationToken);
            return;
        }
        catch (RpcException ex) when (TelegramErrors.IsAuthError(ex))
        {
            await worker.RevokeAsync(ex.Message, cancellationToken);
            return;
        }
        catch (RpcException ex)
        {
            // USERNAME_NOT_OCCUPIED, YOU_BLOCKED_USER и подобное - с ботом что-то не так
            error = $"Telegram error: {ex.Message}";
        }
        catch (ScenarioException ex)
        {
            error = ex.Message;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // обрыв связи с Telegram и прочее на стороне пробы - бот тут ни при чём
            logger.LogWarning(ex, "Job {JobId} for check {CheckId} failed on probe side, result is not published", job.JobId, job.CheckId);
            return;
        }

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

        // MessageId = JobId: дубли отсекает inbox в incidents
        await publisher.PublishAsync(OutgoingMessage.Create(result, result.FinishedAt, messageId: job.JobId), cancellationToken);

        logger.LogInformation(
            "Check {CheckId} {Bot}: {Result} in {DurationMs} ms",
            job.CheckId,
            job.BotUsername,
            error ?? "OK",
            result.DurationMs);
    }
}
