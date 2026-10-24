using System.Threading.Channels;
using BotWatch.Probe.Telegram.Scenarios;
using Channel = System.Threading.Channels.Channel;
using BotWatch.Probe.Telegram.Telegram;
using TL;
using WTelegram;

namespace BotWatch.Probe.Telegram.Accounts;

internal sealed class TelegramBotChat(AccountWorker worker, Client client, User bot) : IBotChat
{
    private readonly Channel<BotReply> _replies = Channel.CreateUnbounded<BotReply>();

    public long BotId => bot.id;

    // вызывается из UpdateManager: новые сообщения бота и правки старых (бот часто правит сообщение после нажатия кнопки)
    public void Receive(Message message)
    {
        _replies.Writer.TryWrite(ToReply(message));
    }

    public async Task SendTextAsync(string text, CancellationToken cancellationToken)
    {
        await BeforeSendAsync(cancellationToken);
        await client.SendMessageAsync(bot, text);
    }

    public async Task SendVoiceAsync(byte[] ogg, CancellationToken cancellationToken)
    {
        await BeforeSendAsync(cancellationToken);

        using var stream = new MemoryStream(ogg);
        var file = await client.UploadFileAsync(stream, "voice.ogg");
        var media = new InputMediaUploadedDocument
        {
            file = file,
            mime_type = "audio/ogg",
            attributes =
            [
                new DocumentAttributeAudio
                {
                    flags = DocumentAttributeAudio.Flags.voice,
                    duration = OggDuration.Seconds(ogg)
                }
            ]
        };

        await client.SendMessageAsync(bot, null, media);
    }

    public async Task ClickAsync(BotReply reply, BotButton button, CancellationToken cancellationToken)
    {
        if (!button.IsInline)
        {
            await SendTextAsync(button.Text, cancellationToken);
            return;
        }

        if (button.CallbackData is null)
        {
            throw new ScenarioException($"Button '{button.Text}' is a link or similar, it can't be clicked");
        }

        await BeforeSendAsync(cancellationToken);
        try
        {
            await client.Messages_GetBotCallbackAnswer(bot, reply.MessageId, button.CallbackData);
        }
        catch (RpcException ex) when (ex.Message == "BOT_RESPONSE_TIMEOUT")
        {
            // бот не подтвердил нажатие. так делают и живые боты, судим по следующему ExpectReply
        }
    }

    public async Task<(BotReply? Match, BotReply? Last)> WaitReplyAsync(
        Func<BotReply, bool> match,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        BotReply? last = null;
        try
        {
            await foreach (var reply in _replies.Reader.ReadAllAsync(timeoutSource.Token))
            {
                last = reply;
                if (match(reply))
                {
                    return (reply, last);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        return (null, last);
    }

    public ValueTask DisposeAsync()
    {
        _replies.Writer.TryComplete();
        worker.CloseChat(this);
        return ValueTask.CompletedTask;
    }

    // ответы на прошлые сообщения ExpectReply видеть не должен
    private async Task BeforeSendAsync(CancellationToken cancellationToken)
    {
        await worker.PaceAsync(cancellationToken);
        while (_replies.Reader.TryRead(out _))
        {
        }
    }

    private static BotReply ToReply(Message message)
    {
        IEnumerable<BotButton> buttons = message.reply_markup switch
        {
            ReplyInlineMarkup inline => inline.rows
                .SelectMany(x => x.buttons)
                .Select(x => new BotButton(x.text, IsInline: true, (x.type as InlineButtonTypeCallback)?.data)),
            ReplyKeyboardMarkup keyboard => keyboard.rows
                .SelectMany(x => x.buttons)
                .Select(x => new BotButton(x.text, IsInline: false)),
            _ => []
        };

        var document = (message.media as MessageMediaDocument)?.document as Document;
        var isVoice = document?.attributes
            .OfType<DocumentAttributeAudio>()
            .Any(x => x.flags.HasFlag(DocumentAttributeAudio.Flags.voice)) == true;

        return new BotReply(
            message.id,
            message.message ?? "",
            [.. buttons],
            isVoice,
            HasFile: document is not null || message.media is MessageMediaPhoto);
    }
}
