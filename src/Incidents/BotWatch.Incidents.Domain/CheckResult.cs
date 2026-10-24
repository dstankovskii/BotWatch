namespace BotWatch.Incidents.Domain;

// история запусков, Id = JobId из monitoring
public class CheckResult
{
    public Guid Id { get; private set; }

    public Guid CheckId { get; private set; }

    public bool Success { get; private set; }

    public int DurationMs { get; private set; }

    public string? Error { get; private set; }

    public string Probe { get; private set; } = null!;

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset FinishedAt { get; private set; }

    private CheckResult()
    {
    }

    public static CheckResult Create(
        Guid jobId,
        Guid checkId,
        bool success,
        int durationMs,
        string? error,
        string probe,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt)
    {
        return new CheckResult
        {
            Id = jobId,
            CheckId = checkId,
            Success = success,
            DurationMs = durationMs,
            Error = error,
            Probe = probe,
            StartedAt = startedAt,
            FinishedAt = finishedAt
        };
    }
}
