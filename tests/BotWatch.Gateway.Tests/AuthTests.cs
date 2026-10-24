using System.Net;
using System.Net.Http.Json;
using BotWatch.Gateway.Auth;
using Microsoft.AspNetCore.Http;

namespace BotWatch.Gateway.Tests;

[Collection(nameof(GatewayCollection))]
public class AuthTests(GatewayFactory factory)
{
    [Fact]
    public async Task Setup_creates_first_user_only_once()
    {
        await factory.ResetUsersAsync();
        var client = await factory.CreateApiClientAsync();

        var state = await client.GetFromJsonAsync<AuthStateDto>("/api/auth/state");
        state!.SetupRequired.ShouldBeTrue();

        var first = await client.PostAsJsonAsync("/api/auth/setup", new CredentialsRequest("admin", "correct-horse"));
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent, await first.Content.ReadAsStringAsync());

        await client.RefreshAntiforgeryAsync();
        state = await client.GetFromJsonAsync<AuthStateDto>("/api/auth/state");
        state.ShouldBe(new AuthStateDto(false, "admin"));

        var second = await client.PostAsJsonAsync("/api/auth/setup", new CredentialsRequest("intruder", "correct-horse"));
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Weak_password_is_rejected_on_setup()
    {
        await factory.ResetUsersAsync();
        var client = await factory.CreateApiClientAsync();

        var response = await client.PostAsJsonAsync("/api/auth/setup", new CredentialsRequest("admin", "short"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Mutating_request_without_antiforgery_token_is_rejected()
    {
        await factory.ResetUsersAsync();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/setup", new CredentialsRequest("admin", "correct-horse"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_and_logout()
    {
        await factory.CreateLoggedInClientAsync();
        var client = await factory.CreateApiClientAsync();

        var wrong = await client.PostAsJsonAsync("/api/auth/login", new CredentialsRequest("admin", "wrong-password"));
        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var ok = await client.PostAsJsonAsync("/api/auth/login", new CredentialsRequest("admin", "correct-horse"));
        ok.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await client.RefreshAntiforgeryAsync();
        (await client.GetFromJsonAsync<AuthStateDto>("/api/auth/state"))!.UserName.ShouldBe("admin");

        var logout = await client.PostAsync("/api/auth/logout", null);
        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<AuthStateDto>("/api/auth/state"))!.UserName.ShouldBeNull();
    }

    [Fact]
    public async Task Account_is_locked_after_five_failed_attempts()
    {
        await factory.CreateLoggedInClientAsync();
        var client = await factory.CreateApiClientAsync();

        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync("/api/auth/login", new CredentialsRequest("admin", "wrong-password"));
        }

        // даже верный пароль не пускает, пока идёт блокировка
        var response = await client.PostAsJsonAsync("/api/auth/login", new CredentialsRequest("admin", "correct-horse"));
        response.StatusCode.ShouldBe((HttpStatusCode)StatusCodes.Status423Locked);
    }

    [Fact]
    public async Task Proxied_api_requires_login()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/monitors");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unknown_api_path_is_404_not_spa()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/nope");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Users_can_add_others_but_not_delete_themselves()
    {
        var client = await factory.CreateLoggedInClientAsync();

        var created = await client.PostAsJsonAsync("/api/users", new CredentialsRequest("second", "correct-horse"));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        var users = await client.GetFromJsonAsync<List<UserDto>>("/api/users") ?? [];
        users.Select(x => x.UserName).ShouldBe(["admin", "second"]);

        var self = users.Single(x => x.UserName == "admin");
        var deleteSelf = await client.DeleteAsync($"/api/users/{self.Id}");
        deleteSelf.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var other = users.Single(x => x.UserName == "second");
        var deleteOther = await client.DeleteAsync($"/api/users/{other.Id}");
        deleteOther.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}

[CollectionDefinition(nameof(GatewayCollection))]
public class GatewayCollection : ICollectionFixture<GatewayFactory>;
