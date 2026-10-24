using System.Net;
using BotWatch.Contracts;

namespace BotWatch.Incidents.Notifications;

public static class AlertMessages
{
    public static string Opened(IncidentOpened incident)
    {
        var cause = string.IsNullOrEmpty(incident.Cause) ? "" : $"\n<code>{Escape(incident.Cause)}</code>";
        return $"🔴 <b>{Escape(incident.MonitorName)}</b> · {Escape(incident.CheckName)} не работает{cause}";
    }

    public static string Resolved(IncidentResolved incident)
    {
        var downtime = incident.ResolvedAt - incident.OpenedAt;
        return $"🟢 <b>{Escape(incident.MonitorName)}</b> · {Escape(incident.CheckName)} снова работает\nЛежал {FormatDuration(downtime)}";
    }

    public static string Stale(CheckStale check)
    {
        var last = check.LastResultAt is null
            ? "результатов ещё не было"
            : $"последний результат {FormatDuration(check.DetectedAt - check.LastResultAt.Value)} назад";

        return $"⚪ <b>{Escape(check.MonitorName)}</b> · {Escape(check.CheckName)}: проверка не выполняется, {last}\n"
            + "Проба молчит или аккаунт отвалился, состояние бота неизвестно";
    }

    public static string Resumed(CheckResumed check)
    {
        return $"🔵 <b>{Escape(check.MonitorName)}</b> · {Escape(check.CheckName)}: проверка снова выполняется\n"
            + $"Молчала {FormatDuration(check.ResumedAt - check.StaleSince)}";
    }

    public static string Test() => "✅ BotWatch: тестовое сообщение, канал настроен";

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes < 1)
        {
            return $"{(int)duration.TotalSeconds} с";
        }

        if (duration.TotalHours < 1)
        {
            return $"{(int)duration.TotalMinutes} мин";
        }

        return $"{(int)duration.TotalHours} ч {duration.Minutes} мин";
    }

    private static string Escape(string value) => WebUtility.HtmlEncode(value);
}
