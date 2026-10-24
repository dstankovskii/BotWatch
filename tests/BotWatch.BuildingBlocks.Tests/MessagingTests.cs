using System.Diagnostics;
using BotWatch.Messaging;
using BotWatch.Messaging.Consuming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace BotWatch.BuildingBlocks.Tests;

[Collection(nameof(InfrastructureCollection))]
public class MessagingTests(InfrastructureFixture fixture)
{
    private readonly string _queue = $"test-{Guid.NewGuid():N}";

    [Fact]
    public async Task Event_from_aggregate_goes_through_outbox_and_is_handled_once()
    {
        using var host = await StartHostAsync();
        var note = Note.Create();

        await WithDbContextAsync(host, async db =>
        {
            db.Notes.Add(note);
            await db.SaveChangesAsync();
        });

        await TestHost.WaitUntilAsync(() => CountHandledAsync(host, note.Id, expected: 1));

        await WithDbContextAsync(host, async db =>
        {
            var outbox = await db.OutboxMessages.Where(x => x.Type == nameof(TestMessage)).ToListAsync();
            outbox.Single(x => x.Payload.Contains(note.Id.ToString())).ProcessedAt.ShouldNotBeNull();
        });

        await host.StopAsync();
    }

    [Fact]
    public async Task Trace_continues_through_outbox_and_broker_to_handler()
    {
        using var source = new ActivitySource("BotWatch.Tests");
        using var listener = new ActivityListener
        {
            ShouldListenTo = x => x.Name is "BotWatch.Tests" or MessagingTelemetry.SourceName,
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using var host = await StartHostAsync();
        var note = Note.Create();
        ActivityTraceId traceId;

        // сохраняем внутри «запроса», а публикует релей позже и уже вне его
        using (var request = source.StartActivity("request")!)
        {
            traceId = request.TraceId;
            await WithDbContextAsync(host, async db =>
            {
                db.Notes.Add(note);
                await db.SaveChangesAsync();
            });
        }

        await TestHost.WaitUntilAsync(() => CountHandledAsync(host, note.Id, expected: 1));

        host.Services.GetRequiredService<HandlerState>().TraceIds[note.Id].ShouldBe(traceId);

        await host.StopAsync();
    }

    [Fact]
    public async Task Duplicate_delivery_is_handled_once()
    {
        using var host = await StartHostAsync();
        var noteId = Guid.NewGuid();
        var message = OutgoingMessage.Create(new TestMessage(noteId), DateTimeOffset.UtcNow);
        var publisher = host.Services.GetRequiredService<RabbitPublisher>();

        await publisher.PublishAsync(message, CancellationToken.None);
        await publisher.PublishAsync(message, CancellationToken.None);

        await TestHost.WaitUntilAsync(() => Task.FromResult(GetAttempts(host, noteId) == 1));
        await Task.Delay(TimeSpan.FromSeconds(1));

        GetAttempts(host, noteId).ShouldBe(1);
        (await CountHandledAsync(host, noteId, expected: 1)).ShouldBeTrue();

        await host.StopAsync();
    }

    [Fact]
    public async Task Failed_handler_is_retried_and_its_changes_are_rolled_back()
    {
        using var host = await StartHostAsync();
        var noteId = Guid.NewGuid();
        host.Services.GetRequiredService<HandlerState>().FailFirst[noteId] = 1;

        await host.Services.GetRequiredService<RabbitPublisher>()
            .PublishAsync(OutgoingMessage.Create(new TestMessage(noteId), DateTimeOffset.UtcNow), CancellationToken.None);

        await TestHost.WaitUntilAsync(() => Task.FromResult(GetAttempts(host, noteId) == 2));
        await TestHost.WaitUntilAsync(() => CountHandledAsync(host, noteId, expected: 1));

        await host.StopAsync();
    }

    [Fact]
    public async Task Message_goes_to_dlq_after_max_retries()
    {
        using var host = await StartHostAsync();
        var noteId = Guid.NewGuid();
        host.Services.GetRequiredService<HandlerState>().FailFirst[noteId] = int.MaxValue;

        await host.Services.GetRequiredService<RabbitPublisher>()
            .PublishAsync(OutgoingMessage.Create(new TestMessage(noteId), DateTimeOffset.UtcNow), CancellationToken.None);

        var connection = await host.Services.GetRequiredService<RabbitConnection>().GetAsync(CancellationToken.None);
        await using var channel = await connection.CreateChannelAsync();

        await TestHost.WaitUntilAsync(async () => await channel.MessageCountAsync($"{_queue}.dlq") == 1);

        // первая попытка + 2 ретрая
        GetAttempts(host, noteId).ShouldBe(3);
        (await CountHandledAsync(host, noteId, expected: 0)).ShouldBeTrue();

        await host.StopAsync();
    }

    [Fact]
    public async Task Runtime_consumer_reads_its_routing_key_until_disposed()
    {
        using var host = await StartHostAsync();
        var factory = host.Services.GetRequiredService<RabbitConsumerFactory>();
        var publisher = host.Services.GetRequiredService<RabbitPublisher>();
        var queue = $"{_queue}.runtime";
        var routingKey = $"test.runtime.{Guid.NewGuid():N}";

        var consumer = await factory.StartAsync(
            queue,
            x => x.Handle<TestMessage, TestMessageHandler>(routingKey),
            CancellationToken.None);

        var first = Guid.NewGuid();
        await publisher.PublishAsync(
            OutgoingMessage.Create(new TestMessage(first), DateTimeOffset.UtcNow, routingKey: routingKey),
            CancellationToken.None);

        await TestHost.WaitUntilAsync(() => CountHandledAsync(host, first, expected: 1));

        // остановленный потребитель не читает, сообщение ждёт в очереди
        await consumer.DisposeAsync();

        var second = Guid.NewGuid();
        await publisher.PublishAsync(
            OutgoingMessage.Create(new TestMessage(second), DateTimeOffset.UtcNow, routingKey: routingKey),
            CancellationToken.None);

        var connection = await host.Services.GetRequiredService<RabbitConnection>().GetAsync(CancellationToken.None);
        await using (var channel = await connection.CreateChannelAsync())
        {
            await TestHost.WaitUntilAsync(async () => await channel.MessageCountAsync(queue) == 1);
        }

        GetAttempts(host, second).ShouldBe(0);

        await factory.DeleteQueueAsync(queue, CancellationToken.None);

        await using (var channel = await connection.CreateChannelAsync())
        {
            await Should.ThrowAsync<OperationInterruptedException>(() => channel.MessageCountAsync(queue));
        }

        await host.StopAsync();
    }

    private async Task<IHost> StartHostAsync()
    {
        var host = TestHost.Build(fixture, _queue, _ => { });
        await host.StartAsync();

        // ждём, пока потребитель объявит очередь, иначе первое сообщение уйдёт в никуда
        var connection = await host.Services.GetRequiredService<RabbitConnection>().GetAsync(CancellationToken.None);
        await TestHost.WaitUntilAsync(async () =>
        {
            // пассивный declare несуществующей очереди закрывает канал, поэтому каждый раз новый
            await using var channel = await connection.CreateChannelAsync();
            try
            {
                return await channel.ConsumerCountAsync(_queue) == 1;
            }
            catch (OperationInterruptedException)
            {
                return false;
            }
        });

        return host;
    }

    private static int GetAttempts(IHost host, Guid noteId)
    {
        return host.Services.GetRequiredService<HandlerState>().Attempts.GetValueOrDefault(noteId);
    }

    private static async Task<bool> CountHandledAsync(IHost host, Guid noteId, int expected)
    {
        var count = 0;
        await WithDbContextAsync(host, async db => count = await db.HandledNotes.CountAsync(x => x.NoteId == noteId));
        return count == expected;
    }

    private static async Task WithDbContextAsync(IHost host, Func<TestDbContext, Task> action)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<TestDbContext>());
    }
}
