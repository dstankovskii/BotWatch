using System.Security.Cryptography;
using BotWatch.Contracts;
using BotWatch.Domain;
using BotWatch.Incidents.Domain;
using BotWatch.Messaging;
using BotWatch.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BotWatch.Incidents.Infrastructure;

public class IncidentsDbContext(
    DbContextOptions<IncidentsDbContext> options,
    SecretProtector protector) : BotWatchDbContext(options)
{
    public DbSet<CheckState> CheckStates => Set<CheckState>();

    public DbSet<Incident> Incidents => Set<Incident>();

    public DbSet<CheckResult> CheckResults => Set<CheckResult>();

    public DbSet<NotificationChannel> NotificationChannels => Set<NotificationChannel>();

    public DbSet<DailyUptime> DailyUptime => Set<DailyUptime>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CheckState>(builder =>
        {
            builder.ToTable("check_states");
            builder.HasKey(x => x.CheckId);
            builder.Property(x => x.CheckId).ValueGeneratedNever();
            builder.Property(x => x.MonitorName).HasMaxLength(100);
            builder.Property(x => x.CheckName).HasMaxLength(100);
            builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            builder.Property(x => x.LastError).HasMaxLength(1000);
            builder.HasOne(x => x.OpenIncident).WithMany().HasForeignKey(x => x.OpenIncidentId).OnDelete(DeleteBehavior.SetNull);
            builder.HasIndex(x => x.MonitorId);
            builder.Ignore(x => x.DomainEvents);
            builder.Property(x => x.WatchedSince).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<Incident>(builder =>
        {
            builder.ToTable("incidents");
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.Cause).HasMaxLength(1000);
            builder.HasIndex(x => new { x.CheckId, x.OpenedAt });
        });

        modelBuilder.Entity<CheckResult>(builder =>
        {
            builder.ToTable("check_results");
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.Error).HasMaxLength(1000);
            builder.Property(x => x.Probe).HasMaxLength(50);
            builder.HasIndex(x => new { x.CheckId, x.FinishedAt });
        });

        modelBuilder.Entity<DailyUptime>(builder =>
        {
            builder.ToTable("daily_uptime");
            builder.HasKey(x => new { x.CheckId, x.Day });
        });

        modelBuilder.Entity<NotificationChannel>(builder =>
        {
            builder.ToTable("notification_channels");
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.Name).HasMaxLength(100);
            builder.Property(x => x.BotToken).HasConversion(protector.StringConverter());
            builder.Property(x => x.ChatId).HasMaxLength(100);
        });
    }

    protected override OutgoingMessage? MapDomainEvent(IDomainEvent domainEvent, DateTimeOffset occurredAt)
    {
        return domainEvent switch
        {
            IncidentOpenedEvent e => OutgoingMessage.Create(
                new IncidentOpened(e.IncidentId, e.CheckId, e.MonitorId, e.MonitorName, e.CheckName, e.Cause, e.OpenedAt),
                occurredAt),
            IncidentResolvedEvent e => OutgoingMessage.Create(
                new IncidentResolved(e.IncidentId, e.CheckId, e.MonitorId, e.MonitorName, e.CheckName, e.OpenedAt, e.ResolvedAt),
                occurredAt),
            CheckStaleEvent e => OutgoingMessage.Create(
                new CheckStale(e.CheckId, e.MonitorId, e.MonitorName, e.CheckName, e.LastResultAt, e.DetectedAt),
                occurredAt),
            CheckResumedEvent e => OutgoingMessage.Create(
                new CheckResumed(e.CheckId, e.MonitorId, e.MonitorName, e.CheckName, e.StaleSince, e.ResumedAt),
                occurredAt),
            _ => null
        };
    }
}

// для dotnet ef: миграции генерируются без подключения к базе и без настоящего ключа
internal class IncidentsDbContextFactory : IDesignTimeDbContextFactory<IncidentsDbContext>
{
    public IncidentsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IncidentsDbContext>()
            .UseNpgsql("Host=localhost;Database=incidents")
            .UseSnakeCaseNamingConvention()
            .Options;

        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return new IncidentsDbContext(options, new SecretProtector(key));
    }
}
