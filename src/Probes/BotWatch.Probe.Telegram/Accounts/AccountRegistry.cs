using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace BotWatch.Probe.Telegram.Accounts;

// воркеры аккаунтов этой реплики. пишет AccountSupervisor, читает обработчик заданий
internal sealed class AccountRegistry
{
    private readonly ConcurrentDictionary<Guid, AccountWorker> _workers = new();

    public int Count => _workers.Count;

    public ICollection<AccountWorker> All => _workers.Values;

    public bool Contains(Guid accountId) => _workers.ContainsKey(accountId);

    public bool TryGet(Guid accountId, [MaybeNullWhen(false)] out AccountWorker worker) => _workers.TryGetValue(accountId, out worker);

    public void Add(AccountWorker worker) => _workers[worker.AccountId] = worker;

    public void Remove(AccountWorker worker) => _workers.TryRemove(new KeyValuePair<Guid, AccountWorker>(worker.AccountId, worker));
}
