using BotWatch.Contracts;
using BotWatch.Hosting;
using BotWatch.Monitoring.Handlers;
using BotWatch.Messaging;
using BotWatch.Monitoring.Endpoints;
using BotWatch.Monitoring.Infrastructure;
using BotWatch.Monitoring.Scheduling;
using BotWatch.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddBotWatchDefaults();
builder.Services.AddBotWatchDbContext<MonitoringDbContext>(builder.Configuration, "Monitoring");
builder.Services.AddRabbitMq(builder.Configuration);
builder.Services.AddOutboxRelay<MonitoringDbContext>();
builder.Services.AddHostedService<CheckScheduler>();

builder.Services.AddRabbitConsumer("monitoring", consumer => consumer
    .Handle<AccountQrIssued, AccountQrIssuedHandler>()
    .Handle<AccountLoggedIn, AccountLoggedInHandler>()
    .Handle<AccountLoginFailed, AccountLoginFailedHandler>()
    .Handle<AccountThrottled, AccountThrottledHandler>()
    .Handle<AccountRevoked, AccountRevokedHandler>()
    .UseInbox<MonitoringDbContext>());

var app = builder.Build();

app.UseBotWatchDefaults();
app.MapMonitorEndpoints();
app.MapAccountEndpoints();

await app.MigrateDatabaseAsync<MonitoringDbContext>();
await app.RunAsync();
