using System.Threading.RateLimiting;
using BotWatch.Gateway.Auth;
using BotWatch.Gateway.Infrastructure;
using BotWatch.Gateway.Status;
using BotWatch.Hosting;
using BotWatch.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddBotWatchDefaults();

var connectionString = builder.Configuration.GetConnectionString("Gateway")
    ?? throw new InvalidOperationException("Connection string 'Gateway' is not configured");

builder.Services.AddDbContext<GatewayDbContext>(options => options
    .UseNpgsql(connectionString)
    .UseSnakeCaseNamingConvention());
builder.Services.AddHealthChecks().AddDbContextCheck<GatewayDbContext>(tags: ["ready"]);
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddDataProtection()
    .SetApplicationName("BotWatch")
    .PersistKeysToDbContext<GatewayDbContext>();

builder.Services
    .AddIdentityCore<IdentityUser>(options =>
    {
        // длина важнее набора символов (NIST 800-63B), правила "цифра + заглавная" только мешают
        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.User.RequireUniqueEmail = false;
    })
    .AddEntityFrameworkStores<GatewayDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "botwatch";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;

    // это API, а не MVC: вместо редиректа на страницу логина отдаём код, фронт разберётся сам
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

// удалённый пользователь или сменённый пароль выкидывают старые сессии за минуту, а не за 30 по умолчанию
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(1));

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(ProxyRoutes.AuthenticatedPolicy, policy => policy.RequireAuthenticatedUser());

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = AntiforgeryDefaults.HeaderName;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // перебор паролей: 10 попыток за 5 минут с одного IP, плюс lockout самого аккаунта в Identity
    var loginAttempts = builder.Configuration.GetValue("Gateway:LoginAttemptsPer5Minutes", 10);
    options.AddPolicy(AuthEndpoints.LoginRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginAttempts, Window = TimeSpan.FromMinutes(5) }));

    options.AddPolicy(StatusEndpoints.StatusRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
});

// за ingress/обратным прокси реальный IP и https приходят в заголовках.
// включать только если перед шлюзом точно стоит прокси, иначе X-Forwarded-For подделает кто угодно
if (builder.Configuration.GetValue<bool>("Gateway:BehindProxy"))
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<ServiceClient>(client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddReverseProxy().AddBotWatchRoutes(builder.Configuration);

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Gateway:BehindProxy"))
{
    app.UseForwardedHeaders();
}

app.UseBotWatchDefaults();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseApiAntiforgery();

app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapStatusEndpoints();
app.MapReverseProxy();

// неизвестный /api/* иначе улетел бы в SPA и вернул index.html с кодом 200
app.Map("/api/{**rest}", () => TypedResults.NotFound()).AllowAnonymous().ExcludeFromDescription();

await app.MigrateDatabaseAsync<GatewayDbContext>();
await app.RunAsync();
