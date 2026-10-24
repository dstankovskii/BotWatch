using BotWatch.Hosting;
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

var app = builder.Build();

app.UseBotWatchDefaults();
app.MapMonitorEndpoints();

await app.MigrateDatabaseAsync<MonitoringDbContext>();
await app.RunAsync();
