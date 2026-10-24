using TL;

namespace BotWatch.Probe.Telegram.Telegram;

// ошибка в сценарии, которая говорит о проблеме бота, а не пробы
public class ScenarioException(string message) : Exception(message);

// WTelegram просит ввести телефон, код или пароль - без человека не войти
public class LoginRequiresInputException(string message) : Exception(message);

internal static class TelegramErrors
{
    // после этих ошибок сессия мертва, нужен новый вход по QR
    private static readonly HashSet<string> AuthErrors =
    [
        "AUTH_KEY_UNREGISTERED",
        "AUTH_KEY_DUPLICATED",
        "SESSION_REVOKED",
        "SESSION_EXPIRED",
        "USER_DEACTIVATED",
        "USER_DEACTIVATED_BAN"
    ];

    public static bool IsAuthError(RpcException exception) => AuthErrors.Contains(exception.Message);

    public static bool IsFloodWait(RpcException exception) => exception.Code == 420;
}
