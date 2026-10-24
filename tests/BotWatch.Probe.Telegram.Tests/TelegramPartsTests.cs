using System.Buffers.Binary;
using BotWatch.Probe.Telegram.Domain;
using BotWatch.Probe.Telegram.Telegram;

namespace BotWatch.Probe.Telegram.Tests;

public class TelegramPartsTests
{
    [Fact]
    public void Ogg_duration_is_taken_from_last_page()
    {
        var ogg = new byte[200];
        WritePage(ogg, 0, granule: 0);
        WritePage(ogg, 100, granule: 48000 * 7 + 100);

        OggDuration.Seconds(ogg).ShouldBe(8);
    }

    [Fact]
    public void Ogg_without_pages_counts_as_one_second()
    {
        OggDuration.Seconds(new byte[50]).ShouldBe(1);
    }

    [Fact]
    public void Session_is_saved_only_after_change()
    {
        using var store = new SessionStore([1, 2, 3]);

        var buffer = new byte[store.Length];
        store.ReadExactly(buffer);
        buffer.ShouldBe([1, 2, 3]);
        store.TakeChanged().ShouldBeNull();

        store.Write([9, 8, 7, 6], 1, 2);

        store.TakeChanged().ShouldBe([8, 7]);
        store.TakeChanged().ShouldBeNull();
    }

    [Fact]
    public void Relogin_and_removal_start_new_generation()
    {
        var account = ProbeAccount.Create(Guid.NewGuid(), "Probe", DateTimeOffset.UtcNow);
        account.Generation.ShouldBe(1);

        account.OnLoggedIn(42, "Probe", [1]);
        account.RequestLogin("Probe");

        account.Generation.ShouldBe(2);
        account.Session.ShouldBeNull();

        account.MarkForRemoval();
        account.Generation.ShouldBe(3);
    }

    [Fact]
    public void Qr_after_login_is_not_published()
    {
        var account = ProbeAccount.Create(Guid.NewGuid(), "Probe", DateTimeOffset.UtcNow);
        account.OnLoggedIn(42, "Probe", [1]);
        account.ClearDomainEvents();

        account.OnQrIssued("tg://login?token=x", DateTimeOffset.UtcNow);

        account.DomainEvents.ShouldBeEmpty();
    }

    private static void WritePage(byte[] buffer, int offset, long granule)
    {
        "OggS"u8.CopyTo(buffer.AsSpan(offset));
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(offset + 6, 8), granule);
    }
}
