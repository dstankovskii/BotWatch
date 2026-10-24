using System.Text.Json;
using System.Text.Json.Serialization;

namespace BotWatch.Gateway.Status;

// ходит во внутренние API напрямую, мимо YARP и авторизации
public class ServiceClient(HttpClient httpClient, IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Uri _monitoring = ServiceUri(configuration, "Monitoring");
    private readonly Uri _incidents = ServiceUri(configuration, "Incidents");

    public Task<List<PublicMonitor>> GetPublicMonitorsAsync(CancellationToken cancellationToken)
    {
        return GetAsync<List<PublicMonitor>>(new Uri(_monitoring, "api/monitors/public"), cancellationToken);
    }

    public Task<List<CheckState>> GetStatesAsync(CancellationToken cancellationToken)
    {
        return GetAsync<List<CheckState>>(new Uri(_incidents, "api/states"), cancellationToken);
    }

    public Task<List<UptimeDay>> GetUptimeAsync(int days, CancellationToken cancellationToken)
    {
        return GetAsync<List<UptimeDay>>(new Uri(_incidents, $"api/uptime?days={days}"), cancellationToken);
    }

    public static Uri ServiceUri(IConfiguration configuration, string name)
    {
        var address = configuration[$"Services:{name}"]
            ?? throw new InvalidOperationException($"Services:{name} is not configured");

        return new Uri(address.EndsWith('/') ? address : address + "/");
    }

    private async Task<T> GetAsync<T>(Uri uri, CancellationToken cancellationToken)
    {
        return await httpClient.GetFromJsonAsync<T>(uri, JsonOptions, cancellationToken)
            ?? throw new HttpRequestException($"Empty response from {uri}");
    }
}

// свои копии DTO, а не ссылки на проекты сервисов: шлюз не должен тянуть их домен
public record PublicMonitor(Guid Id, string Name, IReadOnlyList<Guid> CheckIds);

// статус строкой: у incidents их больше (Stale), а публике хватает трёх.
// проверка, за которой не удаётся следить, для посетителя - «нет данных»
public record CheckState(Guid CheckId, string Status)
{
    public StatusLevel Level => Status switch
    {
        "Up" => StatusLevel.Up,
        "Down" => StatusLevel.Down,
        _ => StatusLevel.Unknown
    };
}

public record UptimeDay(Guid CheckId, DateOnly Day, long Total, long Failed);
