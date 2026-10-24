using System.Diagnostics;
using System.Diagnostics.Metrics;
using BotWatch.Incidents.Domain;

namespace BotWatch.Incidents.Infrastructure;

// результаты проверок считаем здесь, а не в пробах: сюда приходит каждый результат ровно один раз (inbox)
public sealed class IncidentsMetrics
{
    public const string MeterName = "BotWatch.Incidents";

    // сценарий Telegram с ожиданием ответа идёт минутами, стандартные корзины до 10 секунд не подходят
    private static readonly double[] DurationBuckets = [50, 100, 250, 500, 1000, 2500, 5000, 10_000, 30_000, 60_000, 120_000, 300_000];

    private readonly Counter<long> _checks;
    private readonly Histogram<double> _duration;
    private volatile Dictionary<CheckStatus, int> _states = [];

    public IncidentsMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _checks = meter.CreateCounter<long>("botwatch.checks", description: "Completed check runs");
        _duration = meter.CreateHistogram(
            "botwatch.check.duration",
            unit: "ms",
            description: "Check run duration",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBuckets });

        meter.CreateObservableGauge(
            "botwatch.checks.state",
            () => _states.Select(x => new Measurement<int>(x.Value, new KeyValuePair<string, object?>("status", x.Key.ToString()))),
            description: "Enabled checks by status");
    }

    public void RecordResult(string probe, bool success, int durationMs)
    {
        var tags = new TagList { { "probe", probe }, { "result", success ? "success" : "failure" } };
        _checks.Add(1, tags);
        _duration.Record(durationMs, tags);
    }

    // все статусы, даже с нулём: иначе пропавший статус на графике выглядит как обрыв данных
    public void ReportStates(IEnumerable<CheckState> enabledStates)
    {
        var counts = Enum.GetValues<CheckStatus>().ToDictionary(x => x, _ => 0);
        foreach (var state in enabledStates)
        {
            counts[state.Status]++;
        }

        _states = counts;
    }
}
