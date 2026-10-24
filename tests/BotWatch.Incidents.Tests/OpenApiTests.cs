using BotWatch.Incidents.Infrastructure;
using BotWatch.Tests;

namespace BotWatch.Incidents.Tests;

public class OpenApiTests
{
    [Fact]
    public Task OpenApi_document_matches_snapshot()
    {
        return OpenApiSnapshot.VerifyAsync<IncidentsDbContext>("incidents");
    }
}
