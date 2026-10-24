using BotWatch.Contracts;
using BotWatch.Hosting;
using BotWatch.Incidents.Background;
using BotWatch.Incidents.Endpoints;
using BotWatch.Incidents.Handlers;
using BotWatch.Incidents.Infrastructure;
using BotWatch.Incidents.Notifications;
using BotWatch.Messaging;
using BotWatch.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddBotWatchDefaults();
builder.Services.AddSecretProtector(builder.Configuration);
builder.Services.AddBotWatchDbContext<IncidentsDbContext>(builder.Configuration, "Incidents");
builder.Services.AddRabbitMq(builder.Configuration);
builder.Services.AddOutboxRelay<IncidentsDbContext>();

builder.Services.AddHttpClient<TelegramNotifier>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Telegram:BotApiUrl"] ?? "https://api.telegram.org/");
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddRabbitConsumer("incidents", consumer => consumer
    .Handle<CheckChanged, CheckChangedHandler>()
    .Handle<CheckRemoved, CheckRemovedHandler>()
    .Handle<CheckCompleted, CheckCompletedHandler>()
    .UseInbox<IncidentsDbContext>());

// алерты отдельной очередью: Telegram может тормозить, а результаты проверок должны обрабатываться дальше
builder.Services.AddRabbitConsumer("incidents.notifications", consumer =>
{
    consumer.MaxRetries = 10;
    consumer.RetryDelay = TimeSpan.FromSeconds(30);
    consumer
        .Handle<IncidentOpened, IncidentOpenedHandler>()
        .Handle<IncidentResolved, IncidentResolvedHandler>()
        .Handle<CheckStale, CheckStaleHandler>()
        .Handle<CheckResumed, CheckResumedHandler>()
        .UseInbox<IncidentsDbContext>();
});

builder.Services.AddSingleton<IncidentsMetrics>();
builder.Services.AddHostedService<StaleDetector>();

builder.Services.Configure<HeartbeatOptions>(builder.Configuration.GetSection(HeartbeatOptions.SectionName));
builder.Services.AddHttpClient(nameof(Heartbeat), client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHostedService<Heartbeat>();

builder.Services.Configure<HistoryOptions>(builder.Configuration.GetSection(HistoryOptions.SectionName));
builder.Services.AddScoped<HistoryRollup>();
if (builder.Configuration.GetValue($"{HistoryOptions.SectionName}:{nameof(HistoryOptions.RunInBackground)}", true))
{
    builder.Services.AddHostedService<HistoryRollupService>();
}

var app = builder.Build();

app.UseBotWatchDefaults();
app.MapIncidentEndpoints();
app.MapChannelEndpoints();

await app.MigrateDatabaseAsync<IncidentsDbContext>();

// разовый запуск для Kubernetes CronJob: dotnet BotWatch.Incidents.Api.dll rollup-history
if (args.Contains("rollup-history"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<HistoryRollup>().RunAsync(CancellationToken.None);
    return;
}

await app.RunAsync();
