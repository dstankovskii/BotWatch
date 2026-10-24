using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BotWatch.Persistence;

public class Lease
{
    public string Name { get; private set; } = null!;

    public string Owner { get; private set; } = null!;

    public DateTimeOffset ExpiresAt { get; private set; }

    private Lease()
    {
    }
}

internal class LeaseConfiguration : IEntityTypeConfiguration<Lease>
{
    public void Configure(EntityTypeBuilder<Lease> builder)
    {
        builder.ToTable("leases");
        builder.HasKey(x => x.Name);
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Owner).HasMaxLength(200);
    }
}

public static class LeaseExtensions
{
    // захватить свободную или истёкшую аренду либо продлить свою. false - аренда у кого-то другого
    public static async Task<bool> TryAcquireLeaseAsync(
        this BotWatchDbContext dbContext,
        string name,
        string owner,
        TimeSpan duration,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var expiresAt = now + duration;

        var affected = await dbContext.Database.ExecuteSqlAsync($"""
            insert into leases (name, owner, expires_at)
            values ({name}, {owner}, {expiresAt})
            on conflict (name) do update
            set owner = excluded.owner, expires_at = excluded.expires_at
            where leases.owner = excluded.owner or leases.expires_at < {now}
            """,
            cancellationToken);

        return affected > 0;
    }

    public static Task ReleaseLeaseAsync(
        this BotWatchDbContext dbContext,
        string name,
        string owner,
        CancellationToken cancellationToken)
    {
        return dbContext.Leases
            .Where(x => x.Name == name && x.Owner == owner)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
