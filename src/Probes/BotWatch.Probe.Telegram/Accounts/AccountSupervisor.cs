using BotWatch.Persistence;
using BotWatch.Probe.Telegram.Domain;
using BotWatch.Probe.Telegram.Infrastructure;
using BotWatch.Probe.Telegram.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BotWatch.Probe.Telegram.Accounts;

// раздаёт аккаунты между репликами через Lease: аккаунтом занимается ровно одна реплика.
// две копии одной сессии Telegram считает угоном и убивает ключ (AUTH_KEY_DUPLICATED)
internal sealed class AccountSupervisor(
    IServiceProvider services,
    IServiceScopeFactory scopeFactory,
    AccountRegistry registry,
    IOptions<TelegramOptions> options,
    TimeProvider timeProvider,
    ILogger<AccountSupervisor> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(10);

    // за это время реплика должна продлить аренду. упала - аккаунт подхватит другая через минуту
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);

    private static readonly ProbeAccountStatus[] WorkingStatuses =
    [
        ProbeAccountStatus.PendingLogin,
        ProbeAccountStatus.Active,
        ProbeAccountStatus.Removing
    ];

    private readonly string _owner = $"{Environment.MachineName}/{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Account supervisor {Owner} started, up to {MaxAccounts} accounts", _owner, options.Value.MaxAccounts);

        using var timer = new PeriodicTimer(TickInterval, timeProvider);
        do
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Account supervisor tick failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TgProbeDbContext>();

        // отпускаем аренды сразу, чтобы другая реплика не ждала минуту
        foreach (var worker in registry.All.ToList())
        {
            await StopWorkerAsync(dbContext, worker, releaseLease: true, cancellationToken);
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TgProbeDbContext>();

        var accounts = await dbContext.Accounts
            .AsNoTracking()
            .Select(x => new { x.Id, x.Status, x.Generation })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        foreach (var worker in registry.All.ToList())
        {
            // воркер закончил сам (вход не удался, сессия умерла), аккаунт удалили или перелогинили
            if (worker.IsCompleted
                || !accounts.TryGetValue(worker.AccountId, out var account)
                || account.Generation != worker.Generation)
            {
                await StopWorkerAsync(dbContext, worker, releaseLease: true, cancellationToken);
                continue;
            }

            if (!await TryAcquireAsync(dbContext, worker.AccountId, cancellationToken))
            {
                // аренду перехватили: значит, мы не продлевали её дольше минуты. сессию отдаём без боя
                logger.LogWarning("Lost lease on account {AccountId}", worker.AccountId);
                await StopWorkerAsync(dbContext, worker, releaseLease: false, cancellationToken);
            }
        }

        foreach (var account in accounts.Values)
        {
            if (registry.Count >= options.Value.MaxAccounts)
            {
                break;
            }

            if (!WorkingStatuses.Contains(account.Status)
                || registry.Contains(account.Id)
                || !await TryAcquireAsync(dbContext, account.Id, cancellationToken))
            {
                continue;
            }

            var worker = ActivatorUtilities.CreateInstance<AccountWorker>(services, account.Id, account.Generation);
            registry.Add(worker);
            worker.Start();

            logger.LogInformation("Took account {AccountId} ({Status})", account.Id, account.Status);
        }
    }

    private Task<bool> TryAcquireAsync(TgProbeDbContext dbContext, Guid accountId, CancellationToken cancellationToken)
    {
        return dbContext.TryAcquireLeaseAsync(
            LeaseName(accountId),
            _owner,
            LeaseDuration,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private async Task StopWorkerAsync(
        TgProbeDbContext dbContext,
        AccountWorker worker,
        bool releaseLease,
        CancellationToken cancellationToken)
    {
        registry.Remove(worker);
        await worker.DisposeAsync();

        if (releaseLease)
        {
            await dbContext.ReleaseLeaseAsync(LeaseName(worker.AccountId), _owner, cancellationToken);
        }
    }

    private static string LeaseName(Guid accountId) => $"tg-account:{accountId}";
}
