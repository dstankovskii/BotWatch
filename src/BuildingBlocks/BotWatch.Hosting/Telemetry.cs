using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BotWatch.Hosting;

public static class Telemetry
{
    // метрики слушаются на отдельном порту, который не публикуется наружу: шлюз открыт в интернет,
    // и /metrics на его основном порту показал бы всем имена мониторов и аккаунтов
    public const int DefaultMetricsPort = 9464;

    internal static void AddBotWatchTelemetry(this WebApplicationBuilder builder)
    {
        // стандартная переменная OTel. в Compose это Seq: он принимает трассы по OTLP рядом с логами
        var exportTraces = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(builder.Environment.ApplicationName))
            .WithTracing(tracing =>
            {
                tracing
                    .SetSampler(new ParentBasedSampler(new SkipRootClientSpans()))
                    .AddSource("BotWatch.*", "Npgsql")
                    .AddAspNetCoreInstrumentation(options => options.Filter = context => !IsInfrastructure(context.Request.Path))
                    .AddHttpClientInstrumentation();

                if (exportTraces)
                {
                    tracing.AddOtlpExporter();
                }
            })
            .WithMetrics(metrics => metrics
                .AddMeter("BotWatch.*")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter());
    }

    internal static void MapBotWatchMetrics(this WebApplication app)
    {
        var port = app.Configuration.GetValue("Metrics:Port", DefaultMetricsPort);
        app.MapPrometheusScrapingEndpoint().RequireHost($"*:{port}");
    }

    private static bool IsInfrastructure(PathString path)
    {
        return path.StartsWithSegments("/health") || path.StartsWithSegments("/metrics");
    }

    // фоновые циклы (релей outbox, планировщик) каждую секунду ходят в базу без родительской операции.
    // такие одиночные запросы в трассах - шум, оставляем клиентские спаны только внутри запроса или сообщения
    private sealed class SkipRootClientSpans : Sampler
    {
        public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
        {
            return new SamplingResult(samplingParameters.Kind == ActivityKind.Client
                ? SamplingDecision.Drop
                : SamplingDecision.RecordAndSample);
        }
    }
}
