using BotWatch.Domain;
using BotWatch.Monitoring.Domain;
using BotWatch.Monitoring.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace BotWatch.Monitoring.Endpoints;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var accounts = app.MapGroup("/api/accounts");

        accounts.MapGet("/", GetAccountsAsync);
        accounts.MapPost("/", CreateAccountAsync);
        accounts.MapPost("/{id:guid}/login", RequestLoginAsync);
        accounts.MapDelete("/{id:guid}", DeleteAccountAsync);
        accounts.MapGet("/{id:guid}/qr", GetQrPage);
        accounts.MapGet("/{id:guid}/qr.png", GetQrImageAsync);

        return app;
    }

    private static async Task<List<AccountDto>> GetAccountsAsync(MonitoringDbContext dbContext, CancellationToken cancellationToken)
    {
        var accounts = await dbContext.TelegramAccounts
            .AsNoTracking()
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);

        var checks = await dbContext.Checks
            .AsNoTracking()
            .Where(x => x.AccountId != null)
            .ToListAsync(cancellationToken);

        var byAccount = checks.ToLookup(x => x.AccountId!.Value);

        return [.. accounts.Select(x => AccountDto.From(x, [.. byAccount[x.Id]]))];
    }

    private static async Task<Created<AccountDto>> CreateAccountAsync(
        CreateAccountRequest request,
        MonitoringDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var account = TelegramAccount.Create(request.DisplayName, timeProvider.GetUtcNow());

        dbContext.TelegramAccounts.Add(account);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/accounts/{account.Id}", AccountDto.From(account, []));
    }

    private static async Task<Results<NoContent, NotFound>> RequestLoginAsync(
        Guid id,
        MonitoringDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var account = await dbContext.GetAccountForUpdateAsync(id, cancellationToken);
        if (account is null)
        {
            return TypedResults.NotFound();
        }

        account.RequestLogin();

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAccountAsync(
        Guid id,
        MonitoringDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var account = await dbContext.GetAccountForUpdateAsync(id, cancellationToken);
        if (account is null)
        {
            return TypedResults.NotFound();
        }

        // иначе проверки остались бы без аккаунта и молча перестали бы выполняться
        var checks = await dbContext.Checks.CountAsync(x => x.AccountId == id, cancellationToken);
        if (checks > 0)
        {
            throw new DomainException($"Account is used by {checks} checks, move them to another account first");
        }

        account.Remove();
        dbContext.TelegramAccounts.Remove(account);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    // временная страница, пока нет админки: QR перевыпускается, поэтому страница обновляется сама
    private static ContentHttpResult GetQrPage(Guid id)
    {
        var html = $$"""
            <!doctype html>
            <html>
            <head><meta charset="utf-8"><meta http-equiv="refresh" content="10"><title>Telegram login</title></head>
            <body style="font-family: sans-serif; text-align: center">
            <p>Telegram: Settings → Devices → Link Desktop Device</p>
            <img src="/api/accounts/{{id}}/qr.png" alt="QR code is not ready yet">
            </body>
            </html>
            """;

        return TypedResults.Content(html, "text/html");
    }

    private static async Task<Results<FileContentHttpResult, NotFound>> GetQrImageAsync(
        Guid id,
        MonitoringDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var loginUrl = await dbContext.TelegramAccounts
            .Where(x => x.Id == id)
            .Select(x => x.LoginUrl)
            .SingleOrDefaultAsync(cancellationToken);

        if (loginUrl is null)
        {
            return TypedResults.NotFound();
        }

        using var data = QRCodeGenerator.GenerateQrCode(loginUrl, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(8);

        return TypedResults.File(png, "image/png");
    }
}
