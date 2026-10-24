using System.Globalization;
using BotWatch.Contracts;
using BotWatch.Messaging.Consuming;
using BotWatch.Probe.Telegram.Domain;
using BotWatch.Probe.Telegram.Infrastructure;
using BotWatch.Probe.Telegram.Scenarios;
using BotWatch.Probe.Telegram.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TL;
using WTelegram;

namespace BotWatch.Probe.Telegram.Accounts;

// один аккаунт-проверяльщик, пока у этой реплики есть на него Lease:
// вход по QR или восстановление сессии, затем чтение очереди аккаунта и прогон сценариев.
// Generation - номер поколения аккаунта на момент старта. поменялся (перелогин, удаление) - воркер больше не актуален
internal sealed class AccountWorker(
    Guid accountId,
    int generation,
    IServiceScopeFactory scopeFactory,
    RabbitConsumerFactory consumerFactory,
    IOptions<TelegramOptions> options,
    TimeProvider timeProvider,
    ILogger<AccountWorker> logger) : IAsyncDisposable
{
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SessionFlushInterval = TimeSpan.FromSeconds(10);

    // пауза между отправками, чтобы Telegram не счёл аккаунт спамером
    private static readonly TimeSpan PauseBetweenSends = TimeSpan.FromSeconds(3);

    // FloodWait короче этого WTelegram переждёт сам, длиннее - отдаём наверх и ставим аккаунт на паузу
    private const int FloodRetryThresholdSeconds = 5;

    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _fatal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<string, User> _bots = new(StringComparer.OrdinalIgnoreCase);

    private Task _run = Task.CompletedTask;
    private Client? _client;
    private SessionStore? _session;
    private UpdateManager? _updates;
    private RabbitConsumer? _consumer;
    private TelegramBotChat? _activeChat;
    private bool _loggedIn;
    private DateTimeOffset _lastSentAt;

    public Guid AccountId { get; } = accountId;

    public int Generation { get; } = generation;

    public bool IsCompleted => _run.IsCompleted;

    public DateTimeOffset ThrottledUntil { get; private set; }

    public static string QueueName(Guid accountId) => $"tg-probe.account.{accountId}";

    public void Start()
    {
        _run = Task.Run(() => RunAsync(_stopping.Token));
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        await _run;
        _stopping.Dispose();
    }

    public async Task<IBotChat> OpenChatAsync(string botUsername, CancellationToken cancellationToken)
    {
        var client = _client ?? throw new InvalidOperationException($"Account {AccountId} is not connected");

        if (!_bots.TryGetValue(botUsername, out var bot))
        {
            // resolveUsername у Telegram строго лимитирован, поэтому запоминаем
            var resolved = await client.Contacts_ResolveUsername(botUsername.TrimStart('@'));
            bot = resolved.User ?? throw new ScenarioException($"{botUsername} is not a user");
            if (!bot.IsBot)
            {
                throw new ScenarioException($"{botUsername} is not a bot");
            }

            _bots[botUsername] = bot;
        }

        _activeChat = new TelegramBotChat(this, client, bot);
        return _activeChat;
    }

    public void CloseChat(TelegramBotChat chat)
    {
        if (_activeChat == chat)
        {
            _activeChat = null;
        }
    }

    public async Task PaceAsync(CancellationToken cancellationToken)
    {
        var wait = _lastSentAt + PauseBetweenSends - timeProvider.GetUtcNow();
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, timeProvider, cancellationToken);
        }

        _lastSentAt = timeProvider.GetUtcNow();
    }

    public async Task OnThrottledAsync(int seconds, CancellationToken cancellationToken)
    {
        ThrottledUntil = timeProvider.GetUtcNow().AddSeconds(seconds);
        logger.LogWarning("Account {AccountId} got FloodWait for {Seconds} s", AccountId, seconds);

        await UpdateAccountAsync(x => x.OnThrottled(ThrottledUntil), cancellationToken);
    }

    public async Task RevokeAsync(string reason, CancellationToken cancellationToken)
    {
        logger.LogWarning("Account {AccountId} session is dead: {Reason}", AccountId, reason);

        _loggedIn = false;
        await UpdateAccountAsync(x => x.Revoke(reason), cancellationToken);
        _fatal.TrySetResult();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var account = await LoadAccountAsync(cancellationToken);
            if (account is null || account.Generation != Generation)
            {
                return;
            }

            switch (account.Status)
            {
                case ProbeAccountStatus.Removing:
                    await RemoveAsync(account.Session, cancellationToken);
                    return;

                case ProbeAccountStatus.PendingLogin:
                    _client = CreateClient(null);
                    var user = await LoginWithQrAsync(cancellationToken);
                    if (user is null)
                    {
                        return;
                    }

                    await StartListeningAsync(cancellationToken);
                    _loggedIn = await UpdateAccountAsync(
                        x => x.OnLoggedIn(user.id, FormatName(user), _session!.TakeChanged() ?? _session.Current),
                        cancellationToken);
                    break;

                case ProbeAccountStatus.Active:
                    _client = CreateClient(account.Session);
                    if (!await ResumeAsync(cancellationToken))
                    {
                        return;
                    }

                    _loggedIn = true;
                    await StartListeningAsync(cancellationToken);
                    break;

                default:
                    return;
            }

            await WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // сеть, Telegram или база. супервизор перезапустит воркер на следующем тике
            logger.LogError(ex, "Account {AccountId} worker failed", AccountId);
        }
        finally
        {
            await CleanupAsync();
        }
    }

    // работаем до остановки, попутно сохраняя сессию: WTelegram обновляет её при переподключениях
    private async Task WaitAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(SessionFlushInterval, timeProvider);
        var tick = timer.WaitForNextTickAsync(cancellationToken).AsTask();
        while (await Task.WhenAny(tick, _fatal.Task) == tick && await tick)
        {
            await FlushSessionAsync(cancellationToken);
            tick = timer.WaitForNextTickAsync(cancellationToken).AsTask();
        }
    }

    private async Task<User?> LoginWithQrAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(LoginTimeout);

        logger.LogInformation("Account {AccountId}: waiting for QR login", AccountId);
        try
        {
            return await _client!.LoginWithQRCode(url => _ = PublishQrAsync(url, cancellationToken), ct: timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await FailLoginAsync($"QR code was not scanned in {LoginTimeout.TotalMinutes} minutes", cancellationToken);
        }
        catch (LoginRequiresInputException ex)
        {
            await FailLoginAsync(ex.Message, cancellationToken);
        }
        catch (RpcException ex)
        {
            await FailLoginAsync($"Telegram error: {ex.Message}", cancellationToken);
        }

        return null;
    }

    private async Task PublishQrAsync(string loginUrl, CancellationToken cancellationToken)
    {
        try
        {
            await UpdateAccountAsync(x => x.OnQrIssued(loginUrl, timeProvider.GetUtcNow()), cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Account {AccountId}: failed to publish QR", AccountId);
        }
    }

    private async Task FailLoginAsync(string reason, CancellationToken cancellationToken)
    {
        logger.LogWarning("Account {AccountId} login failed: {Reason}", AccountId, reason);
        await UpdateAccountAsync(x => x.OnLoginFailed(reason), cancellationToken);
    }

    private async Task<bool> ResumeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var user = await _client!.LoginUserIfNeeded();
            logger.LogInformation("Account {AccountId} connected as {Name}", AccountId, FormatName(user));
            return true;
        }
        catch (LoginRequiresInputException ex)
        {
            await RevokeAsync(ex.Message, cancellationToken);
        }
        catch (RpcException ex) when (TelegramErrors.IsAuthError(ex))
        {
            await RevokeAsync(ex.Message, cancellationToken);
        }

        return false;
    }

    private async Task StartListeningAsync(CancellationToken cancellationToken)
    {
        // UpdateManager следит, чтобы обновления не терялись и шли по порядку
        _updates = new UpdateManager(_client!, OnUpdateAsync);
        await _client!.Updates_GetState();

        // очередь читаем только после входа: пока аккаунт не активен, monitoring задания в неё не шлёт
        _consumer = await consumerFactory.StartAsync(
            QueueName(AccountId),
            consumer =>
            {
                // сценарии одного аккаунта строго по очереди, иначе ответы ботов перепутаются
                consumer.PrefetchCount = 1;
                consumer.Concurrency = 1;
                // повтор сценария - повторные сообщения боту. результат не дошёл - лучше пропуск
                consumer.MaxRetries = 0;
                consumer.Handle<TelegramCheckRequested, TelegramCheckHandler>(TelegramCheckRequested.RoutingKeyFor(AccountId));
            },
            cancellationToken);
    }

    private Task OnUpdateAsync(Update update)
    {
        var message = update switch
        {
            UpdateNewMessage x => x.message as Message,
            UpdateEditMessage x => x.message as Message,
            _ => null
        };

        // свои сообщения тоже приходят обновлениями, их пропускаем
        if (message is null || message.flags.HasFlag(Message.Flags.out_) || message.peer_id is not PeerUser peer)
        {
            return Task.CompletedTask;
        }

        var chat = _activeChat;
        if (chat is not null && chat.BotId == peer.user_id)
        {
            chat.Receive(message);
        }

        return Task.CompletedTask;
    }

    private async Task RemoveAsync(byte[]? session, CancellationToken cancellationToken)
    {
        if (session is not null)
        {
            try
            {
                _client = CreateClient(session);
                await _client.LoginUserIfNeeded();
                await _client.Auth_LogOut();
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // сессия могла уже умереть - тогда и выходить не из чего
                logger.LogWarning(ex, "Account {AccountId}: logout failed, removing anyway", AccountId);
            }
        }

        await consumerFactory.DeleteQueueAsync(QueueName(AccountId), cancellationToken);

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TgProbeDbContext>();
        await dbContext.Accounts
            .Where(x => x.Id == AccountId && x.Generation == Generation && x.Status == ProbeAccountStatus.Removing)
            .ExecuteDeleteAsync(cancellationToken);

        logger.LogInformation("Account {AccountId} removed", AccountId);
    }

    private async Task CleanupAsync()
    {
        if (_consumer is not null)
        {
            await _consumer.DisposeAsync();
        }

        try
        {
            await FlushSessionAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Account {AccountId}: failed to save session on stop", AccountId);
        }

        _client?.Dispose();
        _session?.Dispose();
    }

    private async Task FlushSessionAsync(CancellationToken cancellationToken)
    {
        if (!_loggedIn || _session?.TakeChanged() is not { } data)
        {
            return;
        }

        await UpdateAccountAsync(x => x.UpdateSession(data), cancellationToken);
    }

    private Client CreateClient(byte[]? session)
    {
        _session = new SessionStore(session);
        var client = new Client(Config, _session)
        {
            FloodRetryThreshold = FloodRetryThresholdSeconds
        };

        var settings = options.Value;
        if (!string.IsNullOrWhiteSpace(settings.MtProxyUrl))
        {
            client.MTProxyUrl = settings.MtProxyUrl;
        }
        else if (!string.IsNullOrWhiteSpace(settings.Proxy))
        {
            var proxy = new Uri(settings.Proxy);
            client.TcpHandler = (host, port) => Socks5.ConnectAsync(proxy, host, port);
        }

        return client;
    }

    private string? Config(string what)
    {
        return what switch
        {
            "api_id" => options.Value.ApiId.ToString(CultureInfo.InvariantCulture),
            "api_hash" => options.Value.ApiHash,
            "device_model" => "BotWatch probe",
            "app_version" => "BotWatch 1.0",
            "password" => throw new LoginRequiresInputException(
                "Account has a cloud password (2FA). Turn it off for the probe account or use another account"),
            "phone_number" or "verification_code" or "email" or "email_verification_code" or "first_name" or "last_name" =>
                throw new LoginRequiresInputException("Session is not valid anymore, log in again with QR"),
            _ => null
        };
    }

    private async Task<ProbeAccount?> LoadAccountAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TgProbeDbContext>();
        return await dbContext.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == AccountId, cancellationToken);
    }

    // false - аккаунт удалили или перевели в новое поколение, изменения от этого воркера уже не нужны
    private async Task<bool> UpdateAccountAsync(Action<ProbeAccount> change, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TgProbeDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var account = await dbContext.GetAccountForUpdateAsync(AccountId, cancellationToken);
        if (account is null || account.Generation != Generation)
        {
            return false;
        }

        change(account);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static string FormatName(User user)
    {
        var name = $"{user.first_name} {user.last_name}".Trim();
        return user.MainUsername is { Length: > 0 } username ? $"{name} (@{username})" : name;
    }
}
