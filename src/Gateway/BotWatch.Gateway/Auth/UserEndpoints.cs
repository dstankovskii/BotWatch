using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BotWatch.Gateway.Auth;

// ролей нет: любой вошедший может добавить или удалить другого пользователя
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var users = app.MapGroup("/api/users").RequireAuthorization();

        users.MapGet("/", GetUsersAsync);
        users.MapPost("/", CreateUserAsync);
        users.MapDelete("/{id}", DeleteUserAsync);

        return app;
    }

    private static Task<List<UserDto>> GetUsersAsync(UserManager<IdentityUser> userManager, CancellationToken cancellationToken)
    {
        return userManager.Users
            .OrderBy(x => x.UserName)
            .Select(x => new UserDto(x.Id, x.UserName!, x.LockoutEnd))
            .ToListAsync(cancellationToken);
    }

    private static async Task<Results<Created<UserDto>, ValidationProblem>> CreateUserAsync(
        CredentialsRequest request,
        UserManager<IdentityUser> userManager)
    {
        var user = new IdentityUser(request.UserName.Trim());
        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return result.ToValidationProblem();
        }

        return TypedResults.Created($"/api/users/{user.Id}", new UserDto(user.Id, user.UserName!, null));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteUserAsync(
        string id,
        HttpContext context,
        UserManager<IdentityUser> userManager)
    {
        // себя удалять не даём, иначе можно остаться вообще без пользователей
        if (userManager.GetUserId(context.User) == id)
        {
            return TypedResults.Problem("You cannot delete yourself", statusCode: StatusCodes.Status400BadRequest);
        }

        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        await userManager.DeleteAsync(user);
        return TypedResults.NoContent();
    }
}

public record UserDto(string Id, string UserName, DateTimeOffset? LockedOutUntil);
