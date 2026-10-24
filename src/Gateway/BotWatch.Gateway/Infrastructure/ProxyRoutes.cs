using BotWatch.Gateway.Status;
using Yarp.ReverseProxy.Configuration;

namespace BotWatch.Gateway.Infrastructure;

// маршруты в коде, а не в appsettings: адреса сервисов те же, что у ServiceClient, и задаются в одном месте
public static class ProxyRoutes
{
    public const string AuthenticatedPolicy = "authenticated";

    public static IReverseProxyBuilder AddBotWatchRoutes(this IReverseProxyBuilder builder, IConfiguration configuration)
    {
        RouteConfig[] routes =
        [
            Api("monitors", "monitoring", "/api/monitors/{**rest}"),
            Api("accounts", "monitoring", "/api/accounts/{**rest}"),
            Api("states", "incidents", "/api/states"),
            Api("checks", "incidents", "/api/checks/{**rest}"),
            Api("incidents", "incidents", "/api/incidents"),
            Api("uptime", "incidents", "/api/uptime"),
            Api("channels", "incidents", "/api/channels/{**rest}"),

            // всё остальное - SPA из nginx. Order побольше, чтобы /api/* матчились раньше
            new RouteConfig
            {
                RouteId = "admin",
                ClusterId = "admin",
                Order = 1000,
                Match = new RouteMatch { Path = "/{**rest}" }
            }
        ];

        ClusterConfig[] clusters =
        [
            Cluster("monitoring", ServiceClient.ServiceUri(configuration, "Monitoring")),
            Cluster("incidents", ServiceClient.ServiceUri(configuration, "Incidents")),
            Cluster("admin", ServiceClient.ServiceUri(configuration, "Admin"))
        ];

        return builder.LoadFromMemory(routes, clusters);
    }

    private static RouteConfig Api(string id, string cluster, string path)
    {
        return new RouteConfig
        {
            RouteId = id,
            ClusterId = cluster,
            AuthorizationPolicy = AuthenticatedPolicy,
            Match = new RouteMatch { Path = path }
        };
    }

    private static ClusterConfig Cluster(string id, Uri address)
    {
        return new ClusterConfig
        {
            ClusterId = id,
            Destinations = new Dictionary<string, DestinationConfig>
            {
                [id] = new() { Address = address.ToString() }
            }
        };
    }
}
