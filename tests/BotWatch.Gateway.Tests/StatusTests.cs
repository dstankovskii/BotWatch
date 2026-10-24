using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BotWatch.Gateway.Status;

namespace BotWatch.Gateway.Tests;

[Collection(nameof(GatewayCollection))]
public class StatusTests(GatewayFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Status_page_is_anonymous_and_aggregates_public_monitors()
    {
        var site = Guid.NewGuid();
        var siteHttp = Guid.NewGuid();
        var bot = Guid.NewGuid();
        var botStart = Guid.NewGuid();
        var botReply = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        factory.Backends.Respond("http://monitoring/api/monitors/public", $$"""
            [
              { "id": "{{site}}", "name": "Site", "checkIds": ["{{siteHttp}}"] },
              { "id": "{{bot}}", "name": "Bot", "checkIds": ["{{botStart}}", "{{botReply}}"] }
            ]
            """);
        factory.Backends.Respond("http://incidents/api/states", $$"""
            [
              { "checkId": "{{siteHttp}}", "status": "Up", "monitorName": "Site", "lastError": "secret" },
              { "checkId": "{{botStart}}", "status": "Up" },
              { "checkId": "{{botReply}}", "status": "Down" },
              { "checkId": "{{Guid.NewGuid()}}", "status": "Down" }
            ]
            """);
        factory.Backends.Respond("http://incidents/api/uptime?days=90", $$"""
            [
              { "checkId": "{{siteHttp}}", "day": "{{today}}", "total": 10, "failed": 0 },
              { "checkId": "{{botStart}}", "day": "{{today}}", "total": 3, "failed": 0 },
              { "checkId": "{{botReply}}", "day": "{{today}}", "total": 1, "failed": 1 }
            ]
            """);

        var client = factory.CreateClient();
        var page = await client.GetFromJsonAsync<StatusPageDto>("/api/status", Json);

        page!.Status.ShouldBe(StatusLevel.Down);
        page.Monitors.Select(x => (x.Name, x.Status)).ShouldBe([("Site", StatusLevel.Up), ("Bot", StatusLevel.Down)]);

        var botMonitor = page.Monitors.Single(x => x.Name == "Bot");
        botMonitor.Uptime.ShouldBe(75);
        botMonitor.Days.Count.ShouldBe(90);
        botMonitor.Days[^1].Uptime.ShouldBe(75);
        botMonitor.Days[0].Uptime.ShouldBeNull();

        // текст ошибок проверок наружу не уходит
        var raw = await client.GetStringAsync("/api/status");
        raw.ShouldNotContain("secret");
    }
}
