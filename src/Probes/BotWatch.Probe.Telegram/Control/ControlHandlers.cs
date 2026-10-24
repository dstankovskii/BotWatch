using BotWatch.Contracts;
using BotWatch.Messaging.Consuming;
using BotWatch.Probe.Telegram.Domain;
using BotWatch.Probe.Telegram.Infrastructure;

namespace BotWatch.Probe.Telegram.Control;

// команды только меняют строку в базе. сам вход и выход делает воркер, у которого Lease на аккаунт:
// AccountSupervisor увидит новое поколение и перезапустит его
public sealed class AccountLoginRequestedHandler(
    TgProbeDbContext dbContext,
    TimeProvider timeProvider) : IMessageHandler<AccountLoginRequested>
{
    public async Task HandleAsync(AccountLoginRequested message, MessageContext context, CancellationToken cancellationToken)
    {
        var account = await dbContext.GetAccountForUpdateAsync(message.AccountId, cancellationToken);
        if (account is null)
        {
            dbContext.Accounts.Add(ProbeAccount.Create(message.AccountId, message.DisplayName, timeProvider.GetUtcNow()));
        }
        else
        {
            account.RequestLogin(message.DisplayName);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed class AccountRemoveRequestedHandler(TgProbeDbContext dbContext) : IMessageHandler<AccountRemoveRequested>
{
    public async Task HandleAsync(AccountRemoveRequested message, MessageContext context, CancellationToken cancellationToken)
    {
        var account = await dbContext.GetAccountForUpdateAsync(message.AccountId, cancellationToken);
        if (account is null)
        {
            return;
        }

        account.MarkForRemoval();
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
