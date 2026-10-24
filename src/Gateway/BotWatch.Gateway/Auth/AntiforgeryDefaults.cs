using Microsoft.AspNetCore.Antiforgery;

namespace BotWatch.Gateway.Auth;

// схема для SPA: токен лежит в читаемой cookie, фронт кладёт его в заголовок каждого изменяющего запроса
public static class AntiforgeryDefaults
{
    public const string CookieName = "XSRF-TOKEN";
    public const string HeaderName = "X-XSRF-TOKEN";

    // проверяем всё, что меняет данные, в том числе запросы, которые дальше уйдут через YARP
    public static IApplicationBuilder UseApiAntiforgery(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api") && !IsSafe(context.Request.Method))
            {
                var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
                try
                {
                    await antiforgery.ValidateRequestAsync(context);
                }
                catch (AntiforgeryValidationException)
                {
                    await Results.Problem("Antiforgery token is missing or invalid", statusCode: StatusCodes.Status400BadRequest)
                        .ExecuteAsync(context);
                    return;
                }
            }

            await next(context);
        });
    }

    private static bool IsSafe(string method)
    {
        return HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
    }
}
