using System.Diagnostics.Metrics;
using BotWatch.Monitoring.Domain;
using BotWatch.Monitoring.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BotWatch.Monitoring.Scheduling;

// сколько сообщений из часового бюджета аккаунт уже потратил. упирается в лимит - проверки начнут сдвигаться.
// observable gauge не может ходить в базу сам, поэтому раз в 15 секунд кладём снимок
public sealed class AccountMetrics : BackgroundService
{
    public const string MeterName = "BotWatch.Monitoring";

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AccountMetrics> _logger;
    private volatile List<(string Account, int Used)> _snapshot = [];

    public AccountMetrics(
        IServiceScopeFactory scopeFactory,
        IMeterFactory meterFactory,
        TimeProvider timeProvider,
        ILogger<AccountMetrics> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;

        var meter = meterFactory.Create(MeterName);
        meter.CreateObservableGauge(
            "botwatch.account_budget_used",
            () => _snapshot.Select(x => new Measurement<int>(x.Used, new KeyValuePair<string, object?>("account", x.Account))),
            unit: "{message}",
            description: $"Messages sent by the account in the current hour, limit is {TelegramAccount.MaxMessagesPerHour}");
        meter.CreateObservableGauge(
            "botwatch.account_budget_limit",
            () => TelegramAccount.MaxMessagesPerHour,
            unit: "{message}",
            description: "Messages per hour allowed for one account");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval, _timeProvider);
        do
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<MonitoringDbContext>();
                var accounts = await dbContext.TelegramAccounts.AsNoTracking().ToListAsync(stoppingToken);
                var now = _timeProvider.GetUtcNow();

                _snapshot = [.. accounts.Select(x => (x.DisplayName, x.BudgetUsedAt(now)))];
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Failed to refresh account metrics");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
