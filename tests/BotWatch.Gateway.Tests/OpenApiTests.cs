using BotWatch.Gateway.Infrastructure;
using BotWatch.Tests;
using Microsoft.AspNetCore.Hosting;

namespace BotWatch.Gateway.Tests;

public class OpenApiTests
{
    [Fact]
    public Task OpenApi_document_matches_snapshot()
    {
        return OpenApiSnapshot.VerifyAsync<GatewayDbContext>("gateway", builder =>
        {
            builder.UseSetting("Services:Monitoring", "http://monitoring");
            builder.UseSetting("Services:Incidents", "http://incidents");
            builder.UseSetting("Services:Admin", "http://admin");
        });
    }
}
