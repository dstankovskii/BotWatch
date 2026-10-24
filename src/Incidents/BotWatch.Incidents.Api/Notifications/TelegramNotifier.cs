using System.Net;
using BotWatch.Incidents.Domain;

namespace BotWatch.Incidents.Notifications;

public sealed class TelegramNotifier(HttpClient httpClient)
{
    public async Task SendAsync(NotificationChannel channel, string html, CancellationToken cancellationToken)
    {
        // "./" обязателен: без него "bot123:ABC/..." парсится как URI со схемой bot123
        using var response = await httpClient.PostAsJsonAsync(
            $"./bot{channel.BotToken}/sendMessage",
            new
            {
                chat_id = channel.ChatId,
                text = html,
                parse_mode = "HTML",
                link_preview_options = new { is_disabled = true }
            },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // в тексте ответа Bot API пишет причину (chat not found, bot was blocked...), токена там нет
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new TelegramNotificationException(channel.Name, response.StatusCode, body);
        }
    }
}

public class TelegramNotificationException(string channel, HttpStatusCode status, string response)
    : Exception($"Telegram channel '{channel}' returned {(int)status}: {response}");
