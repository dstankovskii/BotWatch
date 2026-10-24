using BotWatch.Domain;
using BotWatch.Incidents.Domain;

namespace BotWatch.Incidents.Tests;

public class NotificationChannelTests
{
    private const string Token = "123456789:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw0";

    [Fact]
    public void Token_is_masked()
    {
        var channel = NotificationChannel.Create("Me", Token, "12345", DateTimeOffset.UtcNow);

        channel.MaskedToken.ShouldBe("123456789:***");
    }

    [Fact]
    public void Update_without_token_keeps_current()
    {
        var channel = NotificationChannel.Create("Me", Token, "12345", DateTimeOffset.UtcNow);

        channel.Update("Team", botToken: null, "-100123", enabled: true);

        channel.BotToken.ShouldBe(Token);
        channel.ChatId.ShouldBe("-100123");
    }

    [Theory]
    [InlineData("not-a-token", "12345")]
    [InlineData(Token, "chat")]
    public void Invalid_values_are_rejected(string token, string chatId)
    {
        Should.Throw<DomainException>(() => NotificationChannel.Create("Me", token, chatId, DateTimeOffset.UtcNow));
    }
}
