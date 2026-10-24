using BotWatch.Contracts;
using BotWatch.Domain;
using BotWatch.Messaging;
using BotWatch.Monitoring.Domain;
using BotWatch.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BotWatch.Monitoring.Infrastructure;

public class MonitoringDbContext(DbContextOptions<MonitoringDbContext> options) : BotWatchDbContext(options)
{
    public DbSet<Monitor> Monitors => Set<Monitor>();

    public DbSet<Check> Checks => Set<Check>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Monitor>(builder =>
        {
            builder.ToTable("monitors");
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.Name).HasMaxLength(100);
            builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            builder.Property(x => x.Target).HasMaxLength(500);
            builder.HasMany(x => x.Checks).WithOne().HasForeignKey(x => x.MonitorId).OnDelete(DeleteBehavior.Cascade);
            builder.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<Check>(builder =>
        {
            builder.ToTable("checks");
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.Name).HasMaxLength(100);
            builder.OwnsOne(x => x.Http, http =>
            {
                http.Property(x => x.Path).HasMaxLength(500);
                http.Property(x => x.Method).HasMaxLength(10);
                http.Property(x => x.BodyContains).HasMaxLength(500);
            });

            // планировщик ищет просроченные проверки
            builder.HasIndex(x => x.NextRunAt).HasFilter("enabled");
        });
    }

    protected override OutgoingMessage? MapDomainEvent(IDomainEvent domainEvent, DateTimeOffset occurredAt)
    {
        return domainEvent switch
        {
            CheckConfigured e => OutgoingMessage.Create(
                new CheckChanged(e.CheckId, e.MonitorId, e.MonitorName, e.CheckName, (int)e.Interval.TotalSeconds, e.Enabled),
                occurredAt),
            CheckDeleted e => OutgoingMessage.Create(new CheckRemoved(e.CheckId, e.MonitorId), occurredAt),
            HttpCheckDue e => OutgoingMessage.Create(
                new HttpCheckRequested(
                    e.JobId,
                    e.CheckId,
                    e.MonitorId,
                    e.Url,
                    e.Settings.Method,
                    e.Settings.ExpectedStatus,
                    e.Settings.BodyContains,
                    (int)e.Settings.Timeout.TotalSeconds,
                    e.RequestedAt,
                    e.ExpiresAt),
                occurredAt),
            _ => null
        };
    }
}

// для dotnet ef: миграции генерируются без подключения к базе
internal class MonitoringDbContextFactory : IDesignTimeDbContextFactory<MonitoringDbContext>
{
    public MonitoringDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MonitoringDbContext>()
            .UseNpgsql("Host=localhost;Database=monitoring")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new MonitoringDbContext(options);
    }
}
