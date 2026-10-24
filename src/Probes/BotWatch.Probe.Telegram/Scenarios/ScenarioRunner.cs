using BotWatch.Contracts;

namespace BotWatch.Probe.Telegram.Scenarios;

public static class ScenarioRunner
{
    private const int MaxQuotedReply = 200;

    // null - сценарий прошёл, иначе текст ошибки для алерта
    public static async Task<string?> RunAsync(
        IBotChat chat,
        IReadOnlyList<ScenarioStep> steps,
        CancellationToken cancellationToken)
    {
        // последний ответ, дождавшийся в ExpectReply - на его кнопки жмёт ClickButton
        BotReply? lastReply = null;

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var number = i + 1;

            switch (step.Kind)
            {
                case ScenarioStepKind.SendText:
                case ScenarioStepKind.SendCommand:
                    await chat.SendTextAsync(step.Text!, cancellationToken);
                    break;

                case ScenarioStepKind.SendRandom:
                    await chat.SendTextAsync(step.Phrases![Random.Shared.Next(step.Phrases.Count)], cancellationToken);
                    break;

                case ScenarioStepKind.SendVoice:
                    await chat.SendVoiceAsync(step.Voice!, cancellationToken);
                    break;

                case ScenarioStepKind.ClickButton:
                    var button = lastReply?.Buttons.FirstOrDefault(x => string.Equals(x.Text.Trim(), step.Text, StringComparison.OrdinalIgnoreCase));
                    if (button is null)
                    {
                        return $"Step {number}: button '{step.Text}' not found in the last reply";
                    }

                    await chat.ClickAsync(lastReply!, button, cancellationToken);
                    break;

                case ScenarioStepKind.ExpectReply:
                    var condition = step.Condition ?? ReplyCondition.Any;
                    var timeout = TimeSpan.FromSeconds(step.TimeoutSeconds ?? 60);
                    var (match, last) = await chat.WaitReplyAsync(
                        reply => ReplyMatcher.Matches(reply, condition, step.Text),
                        timeout,
                        cancellationToken);

                    if (match is null)
                    {
                        return last is null
                            ? $"Step {number}: no reply in {timeout.TotalSeconds} s"
                            : $"Step {number}: expected {ReplyMatcher.Describe(condition, step.Text)}, got: {Quote(last)}";
                    }

                    lastReply = match;
                    break;

                default:
                    return $"Step {number}: unknown step {step.Kind}";
            }
        }

        return null;
    }

    private static string Quote(BotReply reply)
    {
        var text = reply.Text.Length > 0 ? reply.Text : reply.IsVoice ? "[voice]" : reply.HasFile ? "[file]" : "[empty]";
        return text.Length > MaxQuotedReply ? text[..MaxQuotedReply] + "..." : text;
    }
}
