using System.Text.RegularExpressions;
using BotWatch.Domain;

namespace BotWatch.Monitoring.Domain;

public enum StepKind
{
    SendText,
    SendCommand,
    SendRandom,
    SendVoice,
    ClickButton,
    ExpectReply
}

public enum ReplyCondition
{
    Any,
    Contains,
    Regex,
    HasButton,
    IsVoice,
    HasFile
}

// Text: текст сообщения, команда, текст кнопки или что искать в ответе - зависит от Kind
public record ScenarioStep(
    StepKind Kind,
    string? Text = null,
    IReadOnlyList<string>? Phrases = null,
    byte[]? Voice = null,
    int? TimeoutSeconds = null,
    ReplyCondition? Condition = null);

public record TelegramScenario
{
    public const int MaxSteps = 10;
    public const int MaxTextLength = 4096;
    public const int MaxPhrases = 50;
    public const int MaxVoiceBytes = 1024 * 1024;
    public const int MinReplyTimeoutSeconds = 5;
    public const int MaxReplyTimeoutSeconds = 300;

    // пауза между отправками, проба держит её сама. учитываем, чтобы прикинуть длительность сценария
    public static readonly TimeSpan PauseBetweenSends = TimeSpan.FromSeconds(3);

    public IReadOnlyList<ScenarioStep> Steps { get; private init; } = [];

    private TelegramScenario()
    {
    }

    // сколько сообщений отправляет один прогон, по этому числу считается бюджет аккаунта.
    // кнопку тоже считаем: на обычной клавиатуре нажатие - это отправка текста
    public int MessagesPerRun => Steps.Count(x => x.Kind != StepKind.ExpectReply);

    // верхняя оценка: все ожидания до таймаута плюс паузы между отправками
    public TimeSpan MaxDuration =>
        TimeSpan.FromSeconds(Steps.Sum(x => x.TimeoutSeconds ?? 0)) + PauseBetweenSends * MessagesPerRun;

    // загрузка из базы без проверок: правила могли ужесточиться, а старые проверки должны читаться
    public static TelegramScenario Restore(IReadOnlyList<ScenarioStep> steps)
    {
        return new TelegramScenario { Steps = steps };
    }

    public static TelegramScenario Create(IReadOnlyList<ScenarioStep>? steps)
    {
        if (steps is null || steps.Count == 0)
        {
            throw new DomainException("Scenario must have at least one step");
        }

        if (steps.Count > MaxSteps)
        {
            throw new DomainException($"Scenario can have at most {MaxSteps} steps");
        }

        var normalized = new List<ScenarioStep>(steps.Count);
        var hasReply = false;
        for (var i = 0; i < steps.Count; i++)
        {
            var step = NormalizeStep(steps[i], i + 1, hasReply);
            hasReply |= step.Kind == StepKind.ExpectReply;
            normalized.Add(step);
        }

        if (normalized[0].Kind is StepKind.ExpectReply or StepKind.ClickButton)
        {
            throw new DomainException("Scenario must start with sending a message");
        }

        // без ожидания ответа проверка ничего не проверяет, только тратит бюджет
        if (!hasReply)
        {
            throw new DomainException("Scenario must have at least one ExpectReply step");
        }

        return new TelegramScenario { Steps = normalized };
    }

    private static ScenarioStep NormalizeStep(ScenarioStep step, int number, bool hasReplyBefore)
    {
        return step.Kind switch
        {
            StepKind.SendText => new ScenarioStep(StepKind.SendText, Text: RequireText(step.Text, number)),
            StepKind.SendCommand => new ScenarioStep(StepKind.SendCommand, Text: NormalizeCommand(step.Text, number)),
            StepKind.SendRandom => new ScenarioStep(StepKind.SendRandom, Phrases: NormalizePhrases(step.Phrases, number)),
            StepKind.SendVoice => new ScenarioStep(StepKind.SendVoice, Voice: RequireVoice(step.Voice, number)),
            StepKind.ClickButton => hasReplyBefore
                ? new ScenarioStep(StepKind.ClickButton, Text: RequireText(step.Text, number))
                : throw new DomainException($"Step {number}: ClickButton needs an ExpectReply before it to have buttons to click"),
            StepKind.ExpectReply => NormalizeExpectReply(step, number),
            _ => throw new DomainException($"Step {number}: unknown kind {step.Kind}")
        };
    }

    private static ScenarioStep NormalizeExpectReply(ScenarioStep step, int number)
    {
        var timeout = step.TimeoutSeconds ?? 60;
        if (timeout is < MinReplyTimeoutSeconds or > MaxReplyTimeoutSeconds)
        {
            throw new DomainException(
                $"Step {number}: reply timeout must be {MinReplyTimeoutSeconds}-{MaxReplyTimeoutSeconds} seconds");
        }

        var condition = step.Condition ?? ReplyCondition.Any;
        var text = condition switch
        {
            ReplyCondition.Contains => RequireText(step.Text, number),
            ReplyCondition.Regex => RequireRegex(step.Text, number),
            // для HasButton текст необязателен: без него подойдёт любая кнопка
            ReplyCondition.HasButton => string.IsNullOrWhiteSpace(step.Text) ? null : step.Text.Trim(),
            _ => null
        };

        return new ScenarioStep(StepKind.ExpectReply, Text: text, TimeoutSeconds: timeout, Condition: condition);
    }

    private static string RequireText(string? text, int number)
    {
        text = text?.Trim() ?? "";
        if (text.Length is 0 or > MaxTextLength)
        {
            throw new DomainException($"Step {number}: text must be 1-{MaxTextLength} characters long");
        }

        return text;
    }

    private static string NormalizeCommand(string? text, int number)
    {
        var command = RequireText(text, number);
        command = command.StartsWith('/') ? command : "/" + command;
        if (command.Length < 2 || char.IsWhiteSpace(command[1]))
        {
            throw new DomainException($"Step {number}: command must look like /status");
        }

        return command;
    }

    private static IReadOnlyList<string> NormalizePhrases(IReadOnlyList<string>? phrases, int number)
    {
        var result = phrases?
            .Select(x => x?.Trim() ?? "")
            .Where(x => x.Length > 0)
            .Distinct()
            .ToList() ?? [];

        if (result.Count is 0 or > MaxPhrases)
        {
            throw new DomainException($"Step {number}: phrase pool must have 1-{MaxPhrases} phrases");
        }

        if (result.Any(x => x.Length > MaxTextLength))
        {
            throw new DomainException($"Step {number}: phrase is longer than {MaxTextLength} characters");
        }

        return result;
    }

    private static byte[] RequireVoice(byte[]? voice, int number)
    {
        if (voice is null || voice.Length == 0 || voice.Length > MaxVoiceBytes)
        {
            throw new DomainException($"Step {number}: voice must be an ogg file up to {MaxVoiceBytes / 1024} KB");
        }

        // голосовые в Telegram - ogg/opus, по сигнатуре отсекаем случайный mp3 или wav
        if (voice.Length < 4 || voice[0] != 'O' || voice[1] != 'g' || voice[2] != 'g' || voice[3] != 'S')
        {
            throw new DomainException($"Step {number}: voice must be an ogg (opus) file");
        }

        return voice;
    }

    private static string RequireRegex(string? pattern, int number)
    {
        pattern = RequireText(pattern, number);
        try
        {
            _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException ex)
        {
            throw new DomainException($"Step {number}: invalid regex: {ex.Message}");
        }

        return pattern;
    }
}
