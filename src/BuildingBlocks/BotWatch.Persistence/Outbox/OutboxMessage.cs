using BotWatch.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BotWatch.Persistence.Outbox;

public class OutboxMessage
{
    private const int MaxErrorLength = 2000;

    public Guid Id { get; private set; }

    // порядок публикации. по времени нельзя - у событий из одной транзакции время совпадает
    public long Position { get; private set; }

    public string Type { get; private set; } = null!;

    public string Exchange { get; private set; } = null!;

    public string RoutingKey { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    private OutboxMessage()
    {
    }

    public static OutboxMessage From(OutgoingMessage message)
    {
        return new OutboxMessage
        {
            Id = message.MessageId,
            Type = message.Type,
            Exchange = message.Exchange,
            RoutingKey = message.RoutingKey,
            Payload = message.Payload,
            OccurredAt = message.OccurredAt
        };
    }

    public OutgoingMessage ToOutgoing() => new(Id, Type, Payload, Exchange, RoutingKey, OccurredAt);

    public void MarkProcessed(DateTimeOffset now)
    {
        Attempts++;
        ProcessedAt = now;
        LastError = null;
    }

    public void MarkFailed(string error)
    {
        Attempts++;
        LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
    }
}

internal class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Position).UseIdentityAlwaysColumn();
        builder.Property(x => x.Type).HasMaxLength(200);
        builder.Property(x => x.Exchange).HasMaxLength(200);
        builder.Property(x => x.RoutingKey).HasMaxLength(200);
        builder.Property(x => x.Payload).HasColumnType("jsonb");

        // релей всегда ищет неопубликованные, опубликованных со временем станет большинство
        builder.HasIndex(x => x.Position).HasFilter("processed_at is null");
    }
}
