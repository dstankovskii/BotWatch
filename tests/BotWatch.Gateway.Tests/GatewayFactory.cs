using System.Net;
using System.Net.Http.Json;
using BotWatch.Gateway.Auth;
using BotWatch.Gateway.Infrastructure;
using BotWatch.Gateway.Status;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace BotWatch.Gateway.Tests;

public sealed class GatewayFactory : WebApplicationFactory<GatewayDbContext>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    // ответы внутренних сервисов для статус-страницы
    public StubHandler Backends { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Gateway", _postgres.GetConnectionString());
        builder.UseSetting("Services:Monitoring", "http://monitoring");
        builder.UseSetting("Services:Incidents", "http://incidents");
        builder.UseSetting("Services:Admin", "http://admin");
        builder.UseSetting("Gateway:LoginAttemptsPer5Minutes", "1000");

        builder.ConfigureServices(services =>
            services.AddHttpClient<ServiceClient>().ConfigurePrimaryHttpMessageHandler(() => Backends));
    }

    public async Task ResetUsersAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        await dbContext.Users.ExecuteDeleteAsync();
    }

    // клиент с cookie и уже выданным antiforgery-токеном
    public async Task<HttpClient> CreateApiClientAsync()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.RefreshAntiforgeryAsync();
        return client;
    }

    public async Task<HttpClient> CreateLoggedInClientAsync(string userName = "admin", string password = "correct-horse")
    {
        await ResetUsersAsync();

        var client = await CreateApiClientAsync();
        var response = await client.PostAsJsonAsync("/api/auth/setup", new CredentialsRequest(userName, password));
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await client.RefreshAntiforgeryAsync();
        return client;
    }
}

public static class HttpClientExtensions
{
    public static async Task RefreshAntiforgeryAsync(this HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/antiforgery");
        response.EnsureSuccessStatusCode();

        var token = response.Headers.GetValues("Set-Cookie")
            .Select(x => x.Split(';')[0])
            .Single(x => x.StartsWith(AntiforgeryDefaults.CookieName + "="))
            [(AntiforgeryDefaults.CookieName.Length + 1)..];

        client.DefaultRequestHeaders.Remove(AntiforgeryDefaults.HeaderName);
        client.DefaultRequestHeaders.Add(AntiforgeryDefaults.HeaderName, Uri.UnescapeDataString(token));
    }
}

public sealed class StubHandler : HttpMessageHandler
{
    private readonly Dictionary<string, string> _responses = [];

    public int Calls { get; private set; }

    public void Respond(string url, string json)
    {
        _responses[url] = json;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;

        var response = _responses.TryGetValue(request.RequestUri!.ToString(), out var json)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        return Task.FromResult(response);
    }
}
