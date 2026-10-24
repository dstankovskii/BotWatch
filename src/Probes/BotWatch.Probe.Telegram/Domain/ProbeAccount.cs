using BotWatch.Domain;

namespace BotWatch.Probe.Telegram.Domain;

public enum ProbeAccountStatus
{
    PendingLogin,
    Active,
    LoginFailed,
    Revoked,
    // ждёт, пока владелец Lease выйдет из Telegram и удалит очередь
    Removing
}

public class ProbeAccount : AggregateRoot
{
    public Guid Id { get; private set; }

    public string DisplayName { get; private set; } = null!;

    public ProbeAccountStatus Status { get; private set; }

    // сессия WTelegram, в базе зашифрована
    public byte[]? Session { get; private set; }

    public long? TelegramUserId { get; private set; }

    // растёт при каждом перелогине и удалении. воркер со старым номером понимает, что пора остановиться
    public int Generation { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private ProbeAccount()
    {
    }

    public static ProbeAccount Create(Guid id, string displayName, DateTimeOffset now)
    {
        var account = new ProbeAccount
        {
            Id = id,
            DisplayName = displayName,
            CreatedAt = now
        };

        account.RequestLogin(displayName);
        return account;
    }

    public void RequestLogin(string displayName)
    {
        DisplayName = displayName;
        Status = ProbeAccountStatus.PendingLogin;
        Session = null;
        TelegramUserId = null;
        Generation++;
    }

    public void MarkForRemoval()
    {
        Status = ProbeAccountStatus.Removing;
        Generation++;
    }

    public void UpdateSession(byte[] session)
    {
        Session = session;
    }

    public void OnQrIssued(string loginUrl, DateTimeOffset now)
    {
        if (Status == ProbeAccountStatus.PendingLogin)
        {
            Raise(new QrIssued(Id, loginUrl, now));
        }
    }

    public void OnLoggedIn(long telegramUserId, string telegramName, byte[] session)
    {
        if (Status != ProbeAccountStatus.PendingLogin)
        {
            throw new DomainException($"Account {Id} is {Status}, login is not expected");
        }

        Status = ProbeAccountStatus.Active;
        TelegramUserId = telegramUserId;
        Session = session;
        Raise(new LoggedIn(Id, telegramUserId, telegramName));
    }

    public void OnLoginFailed(string reason)
    {
        Status = ProbeAccountStatus.LoginFailed;
        Session = null;
        Raise(new LoginFailed(Id, reason));
    }

    public void OnThrottled(DateTimeOffset until)
    {
        Raise(new Throttled(Id, until));
    }

    public void Revoke(string reason)
    {
        Status = ProbeAccountStatus.Revoked;
        Session = null;
        Raise(new Revoked(Id, reason));
    }
}

public record QrIssued(Guid AccountId, string LoginUrl, DateTimeOffset IssuedAt) : IDomainEvent;

public record LoggedIn(Guid AccountId, long TelegramUserId, string TelegramName) : IDomainEvent;

public record LoginFailed(Guid AccountId, string Reason) : IDomainEvent;

public record Throttled(Guid AccountId, DateTimeOffset Until) : IDomainEvent;

public record Revoked(Guid AccountId, string Reason) : IDomainEvent;
