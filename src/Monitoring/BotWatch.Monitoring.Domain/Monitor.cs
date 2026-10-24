using System.Text.RegularExpressions;
using BotWatch.Domain;

namespace BotWatch.Monitoring.Domain;

public enum MonitorKind
{
    Http,
    TelegramBot
}

public partial class Monitor : AggregateRoot
{
    private const int MaxNameLength = 100;

    private readonly List<Check> _checks = [];

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public MonitorKind Kind { get; private set; }

    // базовый URL сайта или @username бота
    public string Target { get; private set; } = null!;

    public bool Enabled { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<Check> Checks => _checks;

    private Monitor()
    {
    }

    public static Monitor Create(string name, MonitorKind kind, string target, DateTimeOffset now)
    {
        var monitor = new Monitor
        {
            Id = Guid.CreateVersion7(),
            Kind = kind,
            Enabled = true,
            CreatedAt = now
        };

        monitor.Name = NormalizeName(name);
        monitor.Target = NormalizeTarget(kind, target);

        return monitor;
    }

    public void Update(string name, string target, bool enabled)
    {
        Name = NormalizeName(name);
        Target = NormalizeTarget(Kind, target);
        Enabled = enabled;

        foreach (var check in _checks)
        {
            RaiseCheckConfigured(check);
        }
    }

    public Check AddHttpCheck(string name, TimeSpan interval, HttpCheckSettings settings, DateTimeOffset now)
    {
        EnsureKind(MonitorKind.Http);

        var check = Check.CreateHttp(Id, NormalizeName(name), interval, settings, now);
        _checks.Add(check);
        RaiseCheckConfigured(check);

        return check;
    }

    public void UpdateHttpCheck(
        Guid checkId,
        string name,
        TimeSpan interval,
        HttpCheckSettings settings,
        bool enabled,
        DateTimeOffset now)
    {
        EnsureKind(MonitorKind.Http);

        var check = GetCheck(checkId);
        check.UpdateHttp(NormalizeName(name), interval, settings, enabled, now);
        RaiseCheckConfigured(check);
    }

    // бюджет аккаунта проверяет вызывающий код (TelegramAccount.EnsureFitsBudget): ему нужны проверки всех мониторов
    public Check AddTelegramCheck(
        string name,
        TimeSpan interval,
        Guid accountId,
        TelegramScenario scenario,
        DateTimeOffset now)
    {
        EnsureKind(MonitorKind.TelegramBot);

        var check = Check.CreateTelegram(Id, NormalizeName(name), interval, accountId, scenario, now);
        _checks.Add(check);
        RaiseCheckConfigured(check);

        return check;
    }

    public void UpdateTelegramCheck(
        Guid checkId,
        string name,
        TimeSpan interval,
        Guid accountId,
        TelegramScenario scenario,
        bool enabled,
        DateTimeOffset now)
    {
        EnsureKind(MonitorKind.TelegramBot);

        var check = GetCheck(checkId);
        check.UpdateTelegram(NormalizeName(name), interval, accountId, scenario, enabled, now);
        RaiseCheckConfigured(check);
    }

    public void RemoveCheck(Guid checkId)
    {
        var check = GetCheck(checkId);
        _checks.Remove(check);
        Raise(new CheckDeleted(check.Id, Id));
    }

    // вызывается перед удалением агрегата, чтобы incidents забыл про все проверки
    public void Delete()
    {
        foreach (var check in _checks)
        {
            Raise(new CheckDeleted(check.Id, Id));
        }
    }

    // accounts нужен только Telegram-мониторам: аккаунты должны быть загружены и заблокированы вызывающим
    public int ScheduleDueChecks(DateTimeOffset now, IReadOnlyDictionary<Guid, TelegramAccount>? accounts = null)
    {
        if (!Enabled)
        {
            return 0;
        }

        var scheduled = 0;
        foreach (var check in _checks.Where(x => x.IsDueAt(now)))
        {
            if (Kind == MonitorKind.Http)
            {
                ScheduleHttp(check, now);
                scheduled++;
            }
            else if (TryScheduleTelegram(check, now, accounts))
            {
                scheduled++;
            }
        }

        return scheduled;
    }

    private void ScheduleHttp(Check check, DateTimeOffset now)
    {
        var jobId = check.Schedule(now);
        Raise(new HttpCheckDue(
            jobId,
            check.Id,
            Id,
            Target + check.Http!.Path,
            check.Http,
            now,
            ExpiresAt: now + check.Interval));
    }

    private bool TryScheduleTelegram(Check check, DateTimeOffset now, IReadOnlyDictionary<Guid, TelegramAccount>? accounts)
    {
        if (accounts is null)
        {
            throw new InvalidOperationException("Telegram checks need accounts to be scheduled");
        }

        var accountId = check.AccountId!.Value;
        if (!accounts.TryGetValue(accountId, out var account))
        {
            // удалить используемый аккаунт API не даёт, так что сюда попасть не должны
            check.Postpone(now + check.Interval);
            return false;
        }

        var retryAt = account.TryReserve(check.Scenario!.MessagesPerRun, now);
        if (retryAt is not null)
        {
            check.Postpone(retryAt.Value);
            return false;
        }

        var jobId = check.Schedule(now);
        Raise(new TelegramCheckDue(
            jobId,
            check.Id,
            Id,
            accountId,
            Target,
            check.Scenario,
            now,
            ExpiresAt: now + check.Interval));

        return true;
    }

    private Check GetCheck(Guid checkId)
    {
        return _checks.SingleOrDefault(x => x.Id == checkId)
            ?? throw new CheckNotFoundException(checkId);
    }

    private void EnsureKind(MonitorKind kind)
    {
        if (Kind != kind)
        {
            throw new DomainException($"Monitor '{Name}' is {Kind}, operation requires {kind}");
        }
    }

    private void RaiseCheckConfigured(Check check)
    {
        Raise(new CheckConfigured(check.Id, Id, Name, check.Name, check.Interval, Enabled && check.Enabled));
    }

    private static string NormalizeName(string name)
    {
        name = name?.Trim() ?? "";
        if (name.Length is 0 or > MaxNameLength)
        {
            throw new DomainException($"Name must be 1-{MaxNameLength} characters long");
        }

        return name;
    }

    private static string NormalizeTarget(MonitorKind kind, string target)
    {
        target = target?.Trim() ?? "";

        return kind switch
        {
            MonitorKind.Http => NormalizeUrl(target),
            MonitorKind.TelegramBot => NormalizeBotUsername(target),
            _ => throw new DomainException($"Unknown monitor kind {kind}")
        };
    }

    private static string NormalizeUrl(string target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new DomainException("Target must be an absolute http(s) URL");
        }

        // путь добавляется из проверки, поэтому без завершающего слеша
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static string NormalizeBotUsername(string target)
    {
        var username = target.StartsWith('@') ? target : "@" + target;
        if (!BotUsernameRegex().IsMatch(username))
        {
            throw new DomainException("Target must be a bot username like @my_bot");
        }

        return username;
    }

    // username бота в Telegram всегда заканчивается на bot
    [GeneratedRegex("^@[A-Za-z][A-Za-z0-9_]{1,29}[Bb][Oo][Tt]$")]
    private static partial Regex BotUsernameRegex();
}

public class CheckNotFoundException(Guid checkId) : DomainException($"Check {checkId} not found");
