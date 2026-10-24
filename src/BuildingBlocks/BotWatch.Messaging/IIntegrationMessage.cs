namespace BotWatch.Messaging;

public interface IIntegrationMessage
{
    static abstract MessageRoute Route { get; }
}

public record MessageRoute(string Exchange, string RoutingKey);

public static class Exchanges
{
    public const string Events = "botwatch.events";
    public const string Jobs = "botwatch.jobs";
}
