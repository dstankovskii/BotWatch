using System.Collections.Concurrent;
using BotWatch.Domain;
using BotWatch.Messaging;
using BotWatch.Messaging.Consuming;
using BotWatch.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace BotWatch.BuildingBlocks.Tests;

public static class TestHost
{
    public static IHost Build(InfrastructureFixture fixture, string queue, Action<ConsumerBuilder> configure)
    {
        var rabbitUser = fixture.RabbitUri.UserInfo.Split(':');
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Test"] = fixture.ConnectionString,
            ["RabbitMq:Host"] = fixture.RabbitUri.Host,
            ["RabbitMq:Port"] = fixture.RabbitUri.Port.ToString(),
            ["RabbitMq:Username"] = rabbitUser[0],
            ["RabbitMq:Password"] = rabbitUser[1]
        });

        builder.Services.AddSingleton<HandlerState>();
        builder.Services.AddBotWatchDbContext<TestDbContext>(builder.Configuration, "Test");
        builder.Services.AddRabbitMq(builder.Configuration);
        builder.Services.AddOutboxRelay<TestDbContext>();
        builder.Services.AddRabbitConsumer(queue, consumer =>
        {
            consumer.RetryDelay = TimeSpan.FromMilliseconds(500);
            consumer.MaxRetries = 2;
            consumer.Handle<TestMessage, TestMessageHandler>().UseInbox<TestDbContext>();
            configure(consumer);
        });

        return builder.Build();
    }

    public static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met in time");
            }

            await Task.Delay(100);
        }
    }
}

public record TestMessage(Guid NoteId) : IIntegrationMessage
{
    public static MessageRoute Route { get; } = new(Exchanges.Events, "test.message");
}

public record NoteCreated(Guid NoteId) : IDomainEvent;

public class Note : AggregateRoot
{
    public Guid Id { get; private set; }

    public static Note Create()
    {
        var note = new Note { Id = Guid.CreateVersion7() };
        note.Raise(new NoteCreated(note.Id));
        return note;
    }
}

public class HandledNote
{
    public Guid MessageId { get; set; }

    public Guid NoteId { get; set; }
}

public class TestDbContext(DbContextOptions<TestDbContext> options) : BotWatchDbContext(options)
{
    public DbSet<Note> Notes => Set<Note>();

    public DbSet<HandledNote> HandledNotes => Set<HandledNote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Note>().Property(x => x.Id).ValueGeneratedNever();
        modelBuilder.Entity<HandledNote>().HasKey(x => new { x.MessageId, x.NoteId });
    }

    protected override OutgoingMessage? MapDomainEvent(IDomainEvent domainEvent, DateTimeOffset occurredAt)
    {
        return domainEvent switch
        {
            NoteCreated e => OutgoingMessage.Create(new TestMessage(e.NoteId), occurredAt),
            _ => null
        };
    }
}

public class HandlerState
{
    public ConcurrentDictionary<Guid, int> Attempts { get; } = new();

    // сколько первых попыток для заметки должно упасть
    public ConcurrentDictionary<Guid, int> FailFirst { get; } = new();
}

public class TestMessageHandler(TestDbContext dbContext, HandlerState state) : IMessageHandler<TestMessage>
{
    public async Task HandleAsync(TestMessage message, MessageContext context, CancellationToken cancellationToken)
    {
        var attempt = state.Attempts.AddOrUpdate(message.NoteId, 1, (_, value) => value + 1);

        dbContext.HandledNotes.Add(new HandledNote { MessageId = context.MessageId, NoteId = message.NoteId });
        await dbContext.SaveChangesAsync(cancellationToken);

        // падаем уже после SaveChanges - запись должна откатиться вместе с отметкой в inbox
        if (state.FailFirst.TryGetValue(message.NoteId, out var failures) && attempt <= failures)
        {
            throw new InvalidOperationException($"Planned failure {attempt}");
        }
    }
}
