using BotWatch.Gateway.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BotWatch.Gateway.Auth;

public static class AuthEndpoints
{
    public const string LoginRateLimit = "login";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").AllowAnonymous();

        auth.MapGet("/state", GetStateAsync);
        auth.MapGet("/antiforgery", IssueAntiforgeryToken);
        auth.MapPost("/setup", SetupAsync).RequireRateLimiting(LoginRateLimit);
        auth.MapPost("/login", LoginAsync).RequireRateLimiting(LoginRateLimit);
        auth.MapPost("/logout", LogoutAsync);
        auth.MapPost("/password", ChangePasswordAsync).RequireAuthorization();

        return app;
    }

    private static async Task<AuthStateDto> GetStateAsync(
        HttpContext context,
        UserManager<IdentityUser> userManager,
        CancellationToken cancellationToken)
    {
        var setupRequired = !await userManager.Users.AnyAsync(cancellationToken);
        var userName = context.User.Identity?.IsAuthenticated == true ? context.User.Identity.Name : null;

        return new AuthStateDto(setupRequired, userName);
    }

    // токен привязан к пользователю, поэтому фронт берёт новый после входа и выхода
    private static NoContent IssueAntiforgeryToken(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);

        context.Response.Cookies.Append(AntiforgeryDefaults.CookieName, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps,
            Path = "/"
        });

        return TypedResults.NoContent();
    }

    // мастер первого запуска: пока пользователей нет, кто первый открыл админку, тот и создаёт админа.
    // advisory lock - чтобы два одновременных запроса не создали двух "первых"
    private static async Task<Results<NoContent, Conflict<string>, ValidationProblem>> SetupAsync(
        CredentialsRequest request,
        GatewayDbContext dbContext,
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(4242)", cancellationToken);

        if (await userManager.Users.AnyAsync(cancellationToken))
        {
            return TypedResults.Conflict("Setup is already done");
        }

        var user = new IdentityUser(request.UserName.Trim());
        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return result.ToValidationProblem();
        }

        await transaction.CommitAsync(cancellationToken);
        await signInManager.SignInAsync(user, isPersistent: true);

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> LoginAsync(
        CredentialsRequest request,
        SignInManager<IdentityUser> signInManager)
    {
        var result = await signInManager.PasswordSignInAsync(
            request.UserName.Trim(),
            request.Password,
            isPersistent: true,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return TypedResults.NoContent();
        }

        // не говорим, что именно не так: логин или пароль
        return result.IsLockedOut
            ? TypedResults.Problem("Too many failed attempts, try again later", statusCode: StatusCodes.Status423Locked)
            : TypedResults.Problem("Invalid user name or password", statusCode: StatusCodes.Status401Unauthorized);
    }

    private static async Task<NoContent> LogoutAsync(SignInManager<IdentityUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ValidationProblem, UnauthorizedHttpResult>> ChangePasswordAsync(
        ChangePasswordRequest request,
        HttpContext context,
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return result.ToValidationProblem();
        }

        // security stamp сменился, перевыпускаем свою cookie, чужие сессии отвалятся сами
        await signInManager.RefreshSignInAsync(user);
        return TypedResults.NoContent();
    }

    public static ValidationProblem ToValidationProblem(this IdentityResult result)
    {
        return TypedResults.ValidationProblem(result.Errors
            .GroupBy(x => x.Code.Contains("Password") ? "password" : "userName")
            .ToDictionary(x => x.Key, x => x.Select(e => e.Description).ToArray()));
    }
}

public record CredentialsRequest(string UserName, string Password);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record AuthStateDto(bool SetupRequired, string? UserName);
