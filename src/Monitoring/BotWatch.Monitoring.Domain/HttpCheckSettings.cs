using BotWatch.Domain;

namespace BotWatch.Monitoring.Domain;

public record HttpCheckSettings
{
    private static readonly string[] AllowedMethods = ["GET", "HEAD"];
    private static readonly TimeSpan MaxTimeout = TimeSpan.FromSeconds(60);

    // путь относительно Target монитора, например /health
    public string Path { get; private init; } = "";

    public string Method { get; private init; } = "GET";

    public int ExpectedStatus { get; private init; } = 200;

    public string? BodyContains { get; private init; }

    public TimeSpan Timeout { get; private init; }

    private HttpCheckSettings()
    {
    }

    public static HttpCheckSettings Create(
        string? path,
        string? method,
        int expectedStatus,
        string? bodyContains,
        TimeSpan timeout)
    {
        path = string.IsNullOrWhiteSpace(path) ? "" : path.Trim();
        if (path.Length > 0 && !path.StartsWith('/'))
        {
            throw new DomainException("Path must start with '/'");
        }

        method = string.IsNullOrWhiteSpace(method) ? "GET" : method.Trim().ToUpperInvariant();
        if (!AllowedMethods.Contains(method))
        {
            throw new DomainException($"Method must be one of: {string.Join(", ", AllowedMethods)}");
        }

        if (expectedStatus is < 100 or > 599)
        {
            throw new DomainException("Expected status must be a valid HTTP status code");
        }

        if (timeout <= TimeSpan.Zero || timeout > MaxTimeout)
        {
            throw new DomainException($"Timeout must be between 1 second and {MaxTimeout.TotalSeconds} seconds");
        }

        return new HttpCheckSettings
        {
            Path = path,
            Method = method,
            ExpectedStatus = expectedStatus,
            BodyContains = string.IsNullOrEmpty(bodyContains) ? null : bodyContains,
            Timeout = timeout
        };
    }
}
