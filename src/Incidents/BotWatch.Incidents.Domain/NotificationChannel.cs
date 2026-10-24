using System.Text.RegularExpressions;
using BotWatch.Domain;

namespace BotWatch.Incidents.Domain;

// бот, через которого шлём алерты, и чат, куда шлём
public partial class NotificationChannel
{
    private const int MaxNameLength = 100;

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    // в базе лежит зашифрованным
    public string BotToken { get; private set; } = null!;

    // id пользователя, группы (с минусом) или @channel
    public string ChatId { get; private set; } = null!;

    public bool Enabled { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private NotificationChannel()
    {
    }

    public static NotificationChannel Create(string name, string botToken, string chatId, DateTimeOffset now)
    {
        var channel = new NotificationChannel
        {
            Id = Guid.CreateVersion7(),
            Enabled = true,
            CreatedAt = now
        };

        channel.Update(name, botToken, chatId, enabled: true);
        return channel;
    }

    // токен не обязателен: в админке его не показываем, и при правке имени он не приходит
    public void Update(string name, string? botToken, string chatId, bool enabled)
    {
        name = name?.Trim() ?? "";
        if (name.Length is 0 or > MaxNameLength)
        {
            throw new DomainException($"Name must be 1-{MaxNameLength} characters long");
        }

        botToken = botToken?.Trim() ?? BotToken ?? "";
        if (!BotTokenRegex().IsMatch(botToken))
        {
            throw new DomainException("Bot token must look like 123456:ABC-DEF...");
        }

        chatId = chatId?.Trim() ?? "";
        if (!ChatIdRegex().IsMatch(chatId))
        {
            throw new DomainException("Chat id must be a number or @channel_name");
        }

        Name = name;
        BotToken = botToken;
        ChatId = chatId;
        Enabled = enabled;
    }

    public string MaskedToken => BotToken[..BotToken.IndexOf(':')] + ":***";

    [GeneratedRegex(@"^\d+:[A-Za-z0-9_-]{30,}$")]
    private static partial Regex BotTokenRegex();

    [GeneratedRegex(@"^(-?\d+|@[A-Za-z][A-Za-z0-9_]{3,})$")]
    private static partial Regex ChatIdRegex();
}
