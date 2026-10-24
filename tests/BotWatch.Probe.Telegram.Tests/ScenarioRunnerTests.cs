using BotWatch.Contracts;
using BotWatch.Probe.Telegram.Scenarios;

namespace BotWatch.Probe.Telegram.Tests;

public class ScenarioRunnerTests
{
    private static readonly ScenarioStep Status = new(ScenarioStepKind.SendCommand, Text: "/status");

    [Fact]
    public async Task Passes_when_bot_replies()
    {
        var chat = new FakeChat { OnSend = (chat, _) => chat.Reply("Подписка активна") };

        var error = await ScenarioRunner.RunAsync(chat, [Status, Expect(ReplyCondition.Contains, "активна")], default);

        error.ShouldBeNull();
        chat.Sent.ShouldBe(["/status"]);
    }

    [Fact]
    public async Task Fails_when_bot_is_silent()
    {
        var chat = new FakeChat();

        var error = await ScenarioRunner.RunAsync(chat, [Status, Expect(ReplyCondition.Any)], default);

        error.ShouldBe("Step 2: no reply in 5 s");
    }

    [Fact]
    public async Task Error_quotes_what_bot_actually_said()
    {
        var chat = new FakeChat { OnSend = (chat, _) => chat.Reply("Internal error") };

        var error = await ScenarioRunner.RunAsync(chat, [Status, Expect(ReplyCondition.Contains, "активна")], default);

        error.ShouldBe("Step 2: expected reply containing 'активна', got: Internal error");
    }

    [Fact]
    public async Task Waits_for_matching_reply_among_several()
    {
        // бот сначала пишет «думаю...», а потом присылает голосовое
        var chat = new FakeChat
        {
            OnSend = (chat, _) =>
            {
                chat.Reply("Думаю...");
                chat.Reply("", isVoice: true);
            }
        };

        var error = await ScenarioRunner.RunAsync(
            chat,
            [new(ScenarioStepKind.SendVoice, Voice: [1]), Expect(ReplyCondition.IsVoice)],
            default);

        error.ShouldBeNull();
    }

    [Fact]
    public async Task Clicks_button_from_last_reply()
    {
        var chat = new FakeChat
        {
            OnSend = (chat, text) =>
            {
                if (text == "/menu")
                {
                    chat.Reply("Меню", new BotButton("Голос", IsInline: true, CallbackData: [7]));
                }
            },
            OnClick = (chat, button) => chat.Reply($"Выбрано: {button.Text}")
        };

        var error = await ScenarioRunner.RunAsync(
            chat,
            [
                new(ScenarioStepKind.SendCommand, Text: "/menu"),
                Expect(ReplyCondition.HasButton),
                new(ScenarioStepKind.ClickButton, Text: "голос"),
                Expect(ReplyCondition.Contains, "Выбрано")
            ],
            default);

        error.ShouldBeNull();
        chat.Clicked.ShouldBe(["Голос"]);
    }

    [Fact]
    public async Task Missing_button_fails_scenario()
    {
        var chat = new FakeChat { OnSend = (chat, _) => chat.Reply("Меню без кнопок") };

        var error = await ScenarioRunner.RunAsync(
            chat,
            [
                new(ScenarioStepKind.SendCommand, Text: "/menu"),
                Expect(ReplyCondition.Any),
                new(ScenarioStepKind.ClickButton, Text: "Статус")
            ],
            default);

        error.ShouldBe("Step 3: button 'Статус' not found in the last reply");
    }

    [Fact]
    public async Task Random_step_sends_one_of_phrases()
    {
        var chat = new FakeChat { OnSend = (chat, _) => chat.Reply("ok") };

        await ScenarioRunner.RunAsync(
            chat,
            [new(ScenarioStepKind.SendRandom, Phrases: ["привет", "как дела"]), Expect(ReplyCondition.Any)],
            default);

        chat.Sent.ShouldHaveSingleItem().ShouldBeOneOf("привет", "как дела");
    }

    [Theory]
    [InlineData(ReplyCondition.Regex, @"^Баланс: \d+", "Баланс: 150 ₽", true)]
    [InlineData(ReplyCondition.Regex, @"^Баланс: \d+", "Ошибка", false)]
    [InlineData(ReplyCondition.Contains, "OK", "all ok", true)]
    [InlineData(ReplyCondition.HasFile, null, "text", false)]
    public void Matcher_checks_conditions(ReplyCondition condition, string? text, string reply, bool expected)
    {
        ReplyMatcher.Matches(new BotReply(1, reply, [], IsVoice: false, HasFile: false), condition, text).ShouldBe(expected);
    }

    private static ScenarioStep Expect(ReplyCondition condition, string? text = null) =>
        new(ScenarioStepKind.ExpectReply, Text: text, TimeoutSeconds: 5, Condition: condition);
}
