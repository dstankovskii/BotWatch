using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BotWatch.Persistence.Inbox;

public class InboxMessage
{
    public Guid MessageId { get; private set; }

    // имя очереди: одно и то же сообщение могут обработать разные потребители сервиса
    public string Consumer { get; private set; } = null!;

    public DateTimeOffset ProcessedAt { get; private set; }

    private InboxMessage()
    {
    }
}

internal class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("inbox_messages");
        builder.HasKey(x => new { x.MessageId, x.Consumer });
        builder.Property(x => x.Consumer).HasMaxLength(200);
        builder.HasIndex(x => x.ProcessedAt);
    }
}
