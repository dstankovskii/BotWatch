using BotWatch.Contracts;
using BotWatch.Messaging;
using BotWatch.Messaging.Consuming;
using BotWatch.Monitoring.Domain;
using BotWatch.Monitoring.Infrastructure;

namespace BotWatch.Monitoring.Handlers;

// события аккаунтов от tg-probe. все одинаковые: найти аккаунт, применить, сохранить.
// транзакцию открывает inbox, так что блокировка строки держится до коммита
public abstract class AccountHandler<TMessage>(MonitoringDbContext dbContext, ILogger logger) : IMessageHandler<TMessage>
    where TMessage : IIntegrationMessage
{
    public async Task HandleAsync(TMessage message, MessageContext context, CancellationToken cancellationToken)
    {
        var accountId = GetAccountId(message);
        var account = await dbContext.GetAccountForUpdateAsync(accountId, cancellationToken);
        if (account is null)
        {
            // аккаунт удалили, а событие от пробы ещё было в пути
            logger.LogInformation("{Type} for unknown account {AccountId}, skipping", typeof(TMessage).Name, accountId);
            return;
        }

        Apply(account, message);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Account {AccountId} {DisplayName}: {Type}, status {Status}",
            account.Id,
            account.DisplayName,
            typeof(TMessage).Name,
            account.Status);
    }

    protected abstract Guid GetAccountId(TMessage message);

    protected abstract void Apply(TelegramAccount account, TMessage message);
}

public sealed class AccountQrIssuedHandler(MonitoringDbContext dbContext, ILogger<AccountQrIssuedHandler> logger)
    : AccountHandler<AccountQrIssued>(dbContext, logger)
{
    protected override Guid GetAccountId(AccountQrIssued message) => message.AccountId;

    protected override void Apply(TelegramAccount account, AccountQrIssued message) =>
        account.OnQrIssued(message.LoginUrl, message.IssuedAt);
}

public sealed class AccountLoggedInHandler(MonitoringDbContext dbContext, ILogger<AccountLoggedInHandler> logger)
    : AccountHandler<AccountLoggedIn>(dbContext, logger)
{
    protected override Guid GetAccountId(AccountLoggedIn message) => message.AccountId;

    protected override void Apply(TelegramAccount account, AccountLoggedIn message) =>
        account.OnLoggedIn(message.TelegramUserId, message.TelegramName);
}

public sealed class AccountLoginFailedHandler(MonitoringDbContext dbContext, ILogger<AccountLoginFailedHandler> logger)
    : AccountHandler<AccountLoginFailed>(dbContext, logger)
{
    protected override Guid GetAccountId(AccountLoginFailed message) => message.AccountId;

    protected override void Apply(TelegramAccount account, AccountLoginFailed message) =>
        account.OnLoginFailed(message.Reason);
}

public sealed class AccountThrottledHandler(MonitoringDbContext dbContext, ILogger<AccountThrottledHandler> logger)
    : AccountHandler<AccountThrottled>(dbContext, logger)
{
    protected override Guid GetAccountId(AccountThrottled message) => message.AccountId;

    protected override void Apply(TelegramAccount account, AccountThrottled message) =>
        account.OnThrottled(message.Until);
}

public sealed class AccountRevokedHandler(MonitoringDbContext dbContext, ILogger<AccountRevokedHandler> logger)
    : AccountHandler<AccountRevoked>(dbContext, logger)
{
    protected override Guid GetAccountId(AccountRevoked message) => message.AccountId;

    protected override void Apply(TelegramAccount account, AccountRevoked message) =>
        account.OnRevoked(message.Reason);
}
