using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace BotWatch.Persistence.Outbox;

// сколько секунд ждёт самое старое неопубликованное сообщение. растёт - релей встал или брокер недоступен.
// возраст считается в момент сбора метрики от времени, найденного на прошлом проходе релея:
// если релей начал падать с хвостом в очереди, значение продолжит расти и без новых проходов
internal static class OutboxMetrics
{
    public const string MeterName = "BotWatch.Persistence";

    private static readonly Meter Meter = new(MeterName);
    private static readonly ConcurrentDictionary<string, DateTimeOffset?> OldestPending = new();

    static OutboxMetrics()
    {
        Meter.CreateObservableGauge(
            "botwatch.outbox.lag",
            Observe,
            unit: "s",
            description: "Age of the oldest unpublished outbox message");
    }

    public static void Report(string database, DateTimeOffset? oldestPending) => OldestPending[database] = oldestPending;

    private static IEnumerable<Measurement<double>> Observe()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (database, oldest) in OldestPending)
        {
            var lag = oldest is null ? 0 : Math.Max(0, (now - oldest.Value).TotalSeconds);
            yield return new Measurement<double>(lag, new KeyValuePair<string, object?>("db", database));
        }
    }
}
