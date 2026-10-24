using BotWatch.Contracts;
using BotWatch.Hosting;
using BotWatch.Messaging;
using BotWatch.Probe.Http;

var builder = WebApplication.CreateBuilder(args);

builder.AddBotWatchDefaults();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddRabbitMq(builder.Configuration);

builder.Services
    .AddHttpClient(HttpCheckHandler.ClientName, client =>
    {
        // таймаут задаёт каждая проверка сама
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BotWatch/1.0");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    });

builder.Services.AddRabbitConsumer("http-probe", consumer =>
{
    // проверки почти всё время ждут сеть, последовательно их гонять нельзя
    consumer.Concurrency = 16;
    consumer.PrefetchCount = 32;
    consumer.MaxRetries = 2;
    consumer.Handle<HttpCheckRequested, HttpCheckHandler>();
});

var app = builder.Build();

app.UseBotWatchDefaults();

await app.RunAsync();
