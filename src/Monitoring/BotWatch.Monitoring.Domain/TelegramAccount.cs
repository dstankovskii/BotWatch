using BotWatch.Domain;

namespace BotWatch.Monitoring.Domain;

public enum AccountStatus
{
    PendingLogin,
    Active,
    LoginFailed,
    Revoked
}

// аккаунт-проверяльщик. сессия живёт в tg-probe, здесь - статус из её событий и учёт бюджета
public class TelegramAccount : AggregateRoot
{
    public const int MaxMessagesPerHour = 30;

    // аккаунт ещё не готов - заглядываем снова через минуту, чтобы после входа проверки пошли сразу
    private static readonly TimeSpan NotReadyRetry = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan BudgetWindow = TimeSpan.FromHours(1);

    public Guid Id { get; private set; }

    public string DisplayName { get; private set; } = null!;

    public AccountStatus Status { get; private set; }

    // имя и id в Telegram, появляются после входа
    public string? TelegramName { get; private set; }

    public long? TelegramUserId { get; private set; }

    // ссылка tg://login для QR, пока ждём сканирования
    public string? LoginUrl { get; private set; }

    public DateTimeOffset? LoginUrlIssuedAt { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset? ThrottledUntil { get; private set; }

    public DateTimeOffset BudgetWindowStart { get; private set; }

    public int BudgetUsed { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private TelegramAccount()
    {
    }

    public static TelegramAccount Create(string displayName, DateTimeOffset now)
    {
        var account = new TelegramAccount
        {
            Id = Guid.CreateVersion7(),
            DisplayName = NormalizeName(displayName),
            CreatedAt = now,
            BudgetWindowStart = now
        };

        account.RequestLogin();
        return account;
    }

    public void RequestLogin()
    {
        Status = AccountStatus.PendingLogin;
        LoginUrl = null;
        LoginUrlIssuedAt = null;
        LastError = null;
        Raise(new AccountLoginRequired(Id, DisplayName));
    }

    // вызывается перед удалением агрегата
    public void Remove()
    {
        Raise(new AccountRemoved(Id));
    }

    public void OnQrIssued(string loginUrl, DateTimeOffset issuedAt)
    {
        // QR, пришедший после входа или после перелогина с более свежим кодом, не нужен
        if (Status != AccountStatus.PendingLogin || issuedAt < LoginUrlIssuedAt)
        {
            return;
        }

        LoginUrl = loginUrl;
        LoginUrlIssuedAt = issuedAt;
    }

    public void OnLoggedIn(long telegramUserId, string telegramName)
    {
        Status = AccountStatus.Active;
        TelegramUserId = telegramUserId;
        TelegramName = telegramName;
        LoginUrl = null;
        LoginUrlIssuedAt = null;
        LastError = null;
    }

    public void OnLoginFailed(string reason)
    {
        if (Status != AccountStatus.PendingLogin)
        {
            return;
        }

        Status = AccountStatus.LoginFailed;
        LoginUrl = null;
        LastError = reason;
    }

    public void OnThrottled(DateTimeOffset until)
    {
        if (ThrottledUntil is null || until > ThrottledUntil)
        {
            ThrottledUntil = until;
        }
    }

    public void OnRevoked(string reason)
    {
        Status = AccountStatus.Revoked;
        LastError = reason;
    }

    // null - бюджет списан, можно отправлять. иначе - когда попробовать снова
    public DateTimeOffset? TryReserve(int messages, DateTimeOffset now)
    {
        if (Status != AccountStatus.Active)
        {
            return now + NotReadyRetry;
        }

        if (ThrottledUntil > now)
        {
            return ThrottledUntil;
        }

        if (now >= BudgetWindowStart + BudgetWindow)
        {
            BudgetWindowStart = now;
            BudgetUsed = 0;
        }

        if (BudgetUsed + messages > MaxMessagesPerHour)
        {
            return BudgetWindowStart + BudgetWindow;
        }

        BudgetUsed += messages;
        return null;
    }

    // сколько сообщений в час в среднем отправят проверки с такими расписаниями
    public static double HourlyLoad(IEnumerable<(int Messages, TimeSpan Interval)> checks)
    {
        return checks.Sum(x => x.Messages * BudgetWindow / x.Interval);
    }

    // расписание, которое заведомо не влезает в бюджет, не даём даже сохранить
    public static void EnsureFitsBudget(IEnumerable<(int Messages, TimeSpan Interval)> checks)
    {
        var load = HourlyLoad(checks);
        if (load > MaxMessagesPerHour)
        {
            throw new DomainException(
                $"Account would send ~{Math.Ceiling(load)} messages per hour, limit is {MaxMessagesPerHour}. " +
                "Increase intervals, remove steps or use another account");
        }
    }

    private static string NormalizeName(string name)
    {
        name = name?.Trim() ?? "";
        if (name.Length is 0 or > 100)
        {
            throw new DomainException("Display name must be 1-100 characters long");
        }

        return name;
    }
}
