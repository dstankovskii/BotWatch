using BotWatch.Domain;
using BotWatch.Messaging;
using BotWatch.Persistence.Inbox;
using BotWatch.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;

namespace BotWatch.Persistence;

public abstract class BotWatchDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<Lease> Leases => Set<Lease>();

    // во что превращается доменное событие для других сервисов. null - событие внутреннее
    protected abstract OutgoingMessage? MapDomainEvent(IDomainEvent domainEvent, DateTimeOffset occurredAt);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        MoveDomainEventsToOutbox();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        MoveDomainEventsToOutbox();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new LeaseConfiguration());
    }

    private void MoveDomainEventsToOutbox()
    {
        var aggregates = ChangeTracker.Entries<AggregateRoot>()
            .Select(entry => entry.Entity)
            .Where(aggregate => aggregate.DomainEvents.Count > 0)
            .ToList();

        var now = DateTimeOffset.UtcNow;
        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                var message = MapDomainEvent(domainEvent, now);
                if (message is not null)
                {
                    OutboxMessages.Add(OutboxMessage.From(message));
                }
            }

            aggregate.ClearDomainEvents();
        }
    }
}
