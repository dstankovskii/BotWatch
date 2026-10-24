using System.Security.Cryptography;
using BotWatch.Contracts;
using BotWatch.Domain;
using BotWatch.Messaging;
using BotWatch.Persistence;
using BotWatch.Probe.Telegram.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BotWatch.Probe.Telegram.Infrastructure;

public class TgProbeDbContext(
    DbContextOptions<TgProbeDbContext> options,
    SecretProtector protector) : BotWatchDbContext(options)
{
    public DbSet<ProbeAccount> Accounts => Set<ProbeAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ProbeAccount>(builder =>
        {
            builder.ToTable("accounts");
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.DisplayName).HasMaxLength(100);
            builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            builder.Property(x => x.Session!).HasConversion(protector.BytesConverter());
            builder.Ignore(x => x.DomainEvents);
        });
    }

    protected override OutgoingMessage? MapDomainEvent(IDomainEvent domainEvent, DateTimeOffset occurredAt)
    {
        return domainEvent switch
        {
            QrIssued e => OutgoingMessage.Create(new AccountQrIssued(e.AccountId, e.LoginUrl, e.IssuedAt), occurredAt),
            LoggedIn e => OutgoingMessage.Create(new AccountLoggedIn(e.AccountId, e.TelegramUserId, e.TelegramName), occurredAt),
            LoginFailed e => OutgoingMessage.Create(new AccountLoginFailed(e.AccountId, e.Reason), occurredAt),
            Throttled e => OutgoingMessage.Create(new AccountThrottled(e.AccountId, e.Until), occurredAt),
            Revoked e => OutgoingMessage.Create(new AccountRevoked(e.AccountId, e.Reason), occurredAt),
            _ => null
        };
    }

    // вызывать внутри транзакции
    public async Task<ProbeAccount?> GetAccountForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        return await Accounts
            .FromSql($"select * from accounts where id = {id} for update")
            .SingleOrDefaultAsync(cancellationToken);
    }
}

// для dotnet ef: миграции генерируются без подключения к базе и без настоящего ключа
internal class TgProbeDbContextFactory : IDesignTimeDbContextFactory<TgProbeDbContext>
{
    public TgProbeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TgProbeDbContext>()
            .UseNpgsql("Host=localhost;Database=tgprobe")
            .UseSnakeCaseNamingConvention()
            .Options;

        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return new TgProbeDbContext(options, new SecretProtector(key));
    }
}
