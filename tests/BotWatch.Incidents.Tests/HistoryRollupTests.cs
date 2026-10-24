using System.Security.Cryptography;
using BotWatch.Incidents.Background;
using BotWatch.Incidents.Domain;
using BotWatch.Incidents.Endpoints;
using BotWatch.Incidents.Infrastructure;
using BotWatch.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace BotWatch.Incidents.Tests;

public sealed class HistoryRollupTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly SecretProtector _protector = new(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    private readonly Guid _checkId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Old_results_are_rolled_into_days_and_removed()
    {
        await AddResultsAsync(Today.AddDays(-40), success: true, false);
        await AddResultsAsync(Today.AddDays(-2), success: true, true, true);
        await AddResultsAsync(Today, success: false);

        await RollupAsync();

        await using var dbContext = CreateDbContext();
        (await dbContext.CheckResults.CountAsync()).ShouldBe(4);
        var days = await dbContext.DailyUptime.OrderBy(x => x.Day).ToListAsync();
        days.Select(x => (x.Day, x.Total, x.Failed)).ShouldBe([(Today.AddDays(-40), 2, 1), (Today.AddDays(-2), 3, 0)]);
    }

    [Fact]
    public async Task Uptime_combines_rolled_and_live_days_without_duplicates()
    {
        await AddResultsAsync(Today.AddDays(-40), success: true, false);
        await AddResultsAsync(Today.AddDays(-2), success: true, true);
        await RollupAsync();
        await AddResultsAsync(Today.AddDays(-1), success: false);
        await AddResultsAsync(Today, success: true);

        var uptime = await GetUptimeAsync(days: 90);

        uptime.Select(x => (x.Day, x.Total, x.Failed)).ShouldBe(
        [
            (Today.AddDays(-40), 2L, 1L),
            (Today.AddDays(-2), 2L, 0L),
            (Today.AddDays(-1), 1L, 1L),
            (Today, 1L, 0L)
        ]);
    }

    [Fact]
    public async Task Repeated_rollup_recounts_late_results_for_last_rolled_day()
    {
        await AddResultsAsync(Today.AddDays(-2), success: true);
        await RollupAsync();

        await AddResultsAsync(Today.AddDays(-2), success: false);
        await RollupAsync();
        await RollupAsync();

        await using var dbContext = CreateDbContext();
        var day = await dbContext.DailyUptime.SingleAsync();
        (day.Total, day.Failed).ShouldBe((2, 1));
    }

    private async Task RollupAsync()
    {
        await using var dbContext = CreateDbContext();
        var rollup = new HistoryRollup(
            dbContext,
            Options.Create(new HistoryOptions()),
            new FixedTimeProvider(Now),
            NullLogger<HistoryRollup>.Instance);

        await rollup.RunAsync(CancellationToken.None);
    }

    private async Task<List<UptimeDayDto>> GetUptimeAsync(int days)
    {
        await using var dbContext = CreateDbContext();
        return await IncidentEndpoints.GetUptimeAsync(dbContext, new FixedTimeProvider(Now), days);
    }

    // прогоны с 10:00 раз в минуту: первый флаг - успех первого, остальные - следующих
    private async Task AddResultsAsync(DateOnly day, bool success, params bool[] more)
    {
        await using var dbContext = CreateDbContext();
        var at = new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);

        foreach (var (ok, i) in more.Prepend(success).Select((x, i) => (x, i)))
        {
            var finishedAt = at.AddMinutes(i);
            dbContext.CheckResults.Add(CheckResult.Create(
                Guid.NewGuid(),
                _checkId,
                ok,
                100,
                ok ? null : "timeout",
                "http",
                finishedAt.AddSeconds(-1),
                finishedAt));
        }

        await dbContext.SaveChangesAsync();
    }

    private IncidentsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<IncidentsDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

        return new IncidentsDbContext(options, _protector);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
