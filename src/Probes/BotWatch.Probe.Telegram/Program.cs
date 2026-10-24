using BotWatch.Contracts;
using BotWatch.Hosting;
using BotWatch.Messaging;
using BotWatch.Persistence;
using BotWatch.Probe.Telegram.Accounts;
using BotWatch.Probe.Telegram.Control;
using BotWatch.Probe.Telegram.Infrastructure;
using BotWatch.Probe.Telegram.Telegram;

var builder = WebApplication.CreateBuilder(args);

builder.AddBotWatchDefaults();
builder.Services.AddSecretProtector(builder.Configuration);
builder.Services.AddBotWatchDbContext<TgProbeDbContext>(builder.Configuration, "TgProbe");
builder.Services.AddRabbitMq(builder.Configuration);
builder.Services.AddOutboxRelay<TgProbeDbContext>();

builder.Services.AddOptions<TelegramOptions>()
    .Bind(builder.Configuration.GetSection(TelegramOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<AccountRegistry>();
builder.Services.AddHostedService<AccountSupervisor>();

// команды аккаунтам общие на все реплики: любая запишет изменение в базу, а выполнит владелец Lease
builder.Services.AddRabbitConsumer("tg-probe.control", consumer => consumer
    .Handle<AccountLoginRequested, AccountLoginRequestedHandler>()
    .Handle<AccountRemoveRequested, AccountRemoveRequestedHandler>()
    .UseInbox<TgProbeDbContext>());

var app = builder.Build();

// WTelegram пишет много служебного, нам интересны только предупреждения и ошибки
var telegramLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("WTelegram");
WTelegram.Helpers.Log = (level, message) =>
{
    if (level >= (int)LogLevel.Warning)
    {
        telegramLogger.Log((LogLevel)level, "{Message}", message);
    }
};

app.UseBotWatchDefaults();

await app.MigrateDatabaseAsync<TgProbeDbContext>();
await app.RunAsync();
