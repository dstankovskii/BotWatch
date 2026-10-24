using System.Threading.Channels;
using BotWatch.Probe.Telegram.Scenarios;

namespace BotWatch.Probe.Telegram.Tests;

// бот, который отвечает сразу, прямо при отправке. таймаут ожидания укорочен,
// чтобы тест на молчащего бота не ждал по-настоящему
internal sealed class FakeChat : IBotChat
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromMilliseconds(200);

    private readonly Channel<BotReply> _replies = Channel.CreateUnbounded<BotReply>();
    private int _nextId = 1;

    public Action<FakeChat, string>? OnSend { get; init; }

    public Action<FakeChat, BotButton>? OnClick { get; init; }

    public List<string> Sent { get; } = [];

    public List<string> Clicked { get; } = [];

    public void Reply(string text, params BotButton[] buttons) => Reply(text, isVoice: false, buttons);

    public void Reply(string text, bool isVoice, params BotButton[] buttons)
    {
        _replies.Writer.TryWrite(new BotReply(_nextId++, text, buttons, isVoice, HasFile: isVoice));
    }

    public Task SendTextAsync(string text, CancellationToken cancellationToken)
    {
        Sent.Add(text);
        OnSend?.Invoke(this, text);
        return Task.CompletedTask;
    }

    public Task SendVoiceAsync(byte[] ogg, CancellationToken cancellationToken)
    {
        Sent.Add("[voice]");
        OnSend?.Invoke(this, "[voice]");
        return Task.CompletedTask;
    }

    public Task ClickAsync(BotReply reply, BotButton button, CancellationToken cancellationToken)
    {
        Clicked.Add(button.Text);
        OnClick?.Invoke(this, button);
        return Task.CompletedTask;
    }

    public async Task<(BotReply? Match, BotReply? Last)> WaitReplyAsync(
        Func<BotReply, bool> match,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(WaitLimit);

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
        catch (OperationCanceledException)
        {
        }

        return (null, last);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
