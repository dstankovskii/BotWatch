using BotWatch.Monitoring.Domain;
using Microsoft.EntityFrameworkCore;

namespace BotWatch.Monitoring.Infrastructure;

public static class MonitorQueries
{
    // блокируем строку монитора, чтобы API и планировщик не перетёрли друг другу NextRunAt.
    // вызывать внутри транзакции
    public static async Task<Monitor?> GetForUpdateAsync(
        this MonitoringDbContext dbContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlAsync($"select 1 from monitors where id = {id} for update", cancellationToken);

        return await dbContext.Monitors
            .Include(x => x.Checks)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }
}
