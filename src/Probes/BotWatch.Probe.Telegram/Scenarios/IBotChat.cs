namespace BotWatch.Probe.Telegram.Scenarios;

// IsInline - кнопка под сообщением (callback), иначе кнопка обычной клавиатуры, нажатие = отправка текста
public record BotButton(string Text, bool IsInline, byte[]? CallbackData = null);

public record BotReply(int MessageId, string Text, IReadOnlyList<BotButton> Buttons, bool IsVoice, bool HasFile);

// переписка с одним ботом на время сценария. ответы копятся с последней отправки,
// так что ExpectReply видит ответ, даже если бот успел ответить до начала ожидания
public interface IBotChat : IAsyncDisposable
{
    Task SendTextAsync(string text, CancellationToken cancellationToken);

    Task SendVoiceAsync(byte[] ogg, CancellationToken cancellationToken);

    Task ClickAsync(BotReply reply, BotButton button, CancellationToken cancellationToken);

    // ждёт ответ, подходящий под условие. null - не дождались, last - последний пришедший ответ для текста ошибки
    Task<(BotReply? Match, BotReply? Last)> WaitReplyAsync(
        Func<BotReply, bool> match,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
