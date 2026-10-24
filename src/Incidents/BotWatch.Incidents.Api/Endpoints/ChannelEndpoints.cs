using BotWatch.Incidents.Domain;
using BotWatch.Incidents.Infrastructure;
using BotWatch.Incidents.Notifications;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace BotWatch.Incidents.Endpoints;

public static class ChannelEndpoints
{
    public static IEndpointRouteBuilder MapChannelEndpoints(this IEndpointRouteBuilder app)
    {
        var channels = app.MapGroup("/api/channels");

        channels.MapGet("/", GetChannelsAsync);
        channels.MapPost("/", CreateChannelAsync);
        channels.MapPut("/{id:guid}", UpdateChannelAsync);
        channels.MapDelete("/{id:guid}", DeleteChannelAsync);
        channels.MapPost("/{id:guid}/test", TestChannelAsync);

        return app;
    }

    private static async Task<List<ChannelDto>> GetChannelsAsync(IncidentsDbContext dbContext, CancellationToken cancellationToken)
    {
        var channels = await dbContext.NotificationChannels
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return [.. channels.Select(ChannelDto.From)];
    }

    private static async Task<Created<ChannelDto>> CreateChannelAsync(
        CreateChannelRequest request,
        IncidentsDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var channel = NotificationChannel.Create(request.Name, request.BotToken, request.ChatId, timeProvider.GetUtcNow());

        dbContext.NotificationChannels.Add(channel);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/channels/{channel.Id}", ChannelDto.From(channel));
    }

    private static async Task<Results<Ok<ChannelDto>, NotFound>> UpdateChannelAsync(
        Guid id,
        UpdateChannelRequest request,
        IncidentsDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var channel = await dbContext.NotificationChannels.FindAsync([id], cancellationToken);
        if (channel is null)
        {
            return TypedResults.NotFound();
        }

        channel.Update(request.Name, request.BotToken, request.ChatId, request.Enabled);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(ChannelDto.From(channel));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteChannelAsync(
        Guid id,
        IncidentsDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var deleted = await dbContext.NotificationChannels.Where(x => x.Id == id).ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> TestChannelAsync(
        Guid id,
        IncidentsDbContext dbContext,
        TelegramNotifier notifier,
        CancellationToken cancellationToken)
    {
        var channel = await dbContext.NotificationChannels.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (channel is null)
        {
            return TypedResults.NotFound();
        }

        try
        {
            await notifier.SendAsync(channel, AlertMessages.Test(), cancellationToken);
            return TypedResults.NoContent();
        }
        catch (TelegramNotificationException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

public record CreateChannelRequest(string Name, string BotToken, string ChatId);

// BotToken = null - оставить текущий
public record UpdateChannelRequest(string Name, string? BotToken, string ChatId, bool Enabled);

public record ChannelDto(Guid Id, string Name, string BotToken, string ChatId, bool Enabled, DateTimeOffset CreatedAt)
{
    // токен наружу не отдаём, только id бота для узнаваемости
    public static ChannelDto From(NotificationChannel channel)
    {
        return new ChannelDto(channel.Id, channel.Name, channel.MaskedToken, channel.ChatId, channel.Enabled, channel.CreatedAt);
    }
}
