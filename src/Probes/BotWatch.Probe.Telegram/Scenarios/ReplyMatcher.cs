using System.Text.RegularExpressions;
using BotWatch.Contracts;

namespace BotWatch.Probe.Telegram.Scenarios;

public static class ReplyMatcher
{
    // регулярку пишет пользователь, не даём ей повесить пробу
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    public static bool Matches(BotReply reply, ReplyCondition condition, string? text)
    {
        return condition switch
        {
            ReplyCondition.Any => true,
            ReplyCondition.Contains => reply.Text.Contains(text ?? "", StringComparison.OrdinalIgnoreCase),
            ReplyCondition.Regex => MatchesRegex(reply.Text, text ?? ""),
            ReplyCondition.HasButton => reply.Buttons.Any(x => text is null || x.Text.Contains(text, StringComparison.OrdinalIgnoreCase)),
            ReplyCondition.IsVoice => reply.IsVoice,
            ReplyCondition.HasFile => reply.HasFile,
            _ => false
        };
    }

    public static string Describe(ReplyCondition condition, string? text)
    {
        return condition switch
        {
            ReplyCondition.Any => "any reply",
            ReplyCondition.Contains => $"reply containing '{text}'",
            ReplyCondition.Regex => $"reply matching /{text}/",
            ReplyCondition.HasButton => text is null ? "reply with buttons" : $"reply with button '{text}'",
            ReplyCondition.IsVoice => "voice reply",
            ReplyCondition.HasFile => "reply with a file",
            _ => condition.ToString()
        };
    }

    private static bool MatchesRegex(string input, string pattern)
    {
        try
        {
            return Regex.IsMatch(input, pattern, RegexOptions.None, RegexTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
