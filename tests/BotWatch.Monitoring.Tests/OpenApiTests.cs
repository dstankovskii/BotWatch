using BotWatch.Monitoring.Infrastructure;
using BotWatch.Tests;

namespace BotWatch.Monitoring.Tests;

public class OpenApiTests
{
    [Fact]
    public Task OpenApi_document_matches_snapshot()
    {
        return OpenApiSnapshot.VerifyAsync<MonitoringDbContext>("monitoring");
    }
}
