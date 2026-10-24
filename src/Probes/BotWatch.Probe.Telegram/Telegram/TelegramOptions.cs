using System.ComponentModel.DataAnnotations;

namespace BotWatch.Probe.Telegram.Telegram;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    // приложение с my.telegram.org. одно на все аккаунты-проверяльщики
    [Range(1, int.MaxValue, ErrorMessage = "Telegram:ApiId is not configured, get it at https://my.telegram.org")]
    public int ApiId { get; set; }

    [Required(ErrorMessage = "Telegram:ApiHash is not configured, get it at https://my.telegram.org")]
    public string ApiHash { get; set; } = "";

    // socks5://user:pass@host:port - если Telegram с сервера недоступен напрямую
    public string? Proxy { get; set; }

    // https://t.me/proxy?server=...&port=...&secret=... - вместо Proxy
    public string? MtProxyUrl { get; set; }

    // сколько аккаунтов берёт одна реплика. остальные достанутся другим репликам
    [Range(1, 1000)]
    public int MaxAccounts { get; set; } = 10;
}
