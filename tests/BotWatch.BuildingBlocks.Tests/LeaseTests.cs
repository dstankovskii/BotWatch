using BotWatch.Persistence;

namespace BotWatch.BuildingBlocks.Tests;

[Collection(nameof(InfrastructureCollection))]
public class LeaseTests(InfrastructureFixture fixture)
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Lease_is_exclusive_until_it_expires()
    {
        using var host = TestHost.Build(fixture, "lease", _ => { });
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var name = $"account-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;

        (await db.TryAcquireLeaseAsync(name, "pod-a", Duration, now, CancellationToken.None)).ShouldBeTrue();
        (await db.TryAcquireLeaseAsync(name, "pod-b", Duration, now.AddSeconds(10), CancellationToken.None)).ShouldBeFalse();

        // своя аренда продлевается
        (await db.TryAcquireLeaseAsync(name, "pod-a", Duration, now.AddSeconds(20), CancellationToken.None)).ShouldBeTrue();
        (await db.TryAcquireLeaseAsync(name, "pod-b", Duration, now.AddSeconds(40), CancellationToken.None)).ShouldBeFalse();

        // pod-a перестал продлевать - аренду забирает pod-b
        (await db.TryAcquireLeaseAsync(name, "pod-b", Duration, now.AddSeconds(51), CancellationToken.None)).ShouldBeTrue();
    }

    [Fact]
    public async Task Released_lease_can_be_taken_immediately()
    {
        using var host = TestHost.Build(fixture, "lease", _ => { });
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var name = $"account-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;

        (await db.TryAcquireLeaseAsync(name, "pod-a", Duration, now, CancellationToken.None)).ShouldBeTrue();
        await db.ReleaseLeaseAsync(name, "pod-a", CancellationToken.None);

        (await db.TryAcquireLeaseAsync(name, "pod-b", Duration, now, CancellationToken.None)).ShouldBeTrue();
    }
}
