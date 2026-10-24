using BotWatch.Incidents.Domain;
using Microsoft.EntityFrameworkCore;

namespace BotWatch.Incidents.Infrastructure;

public static class CheckStateQueries
{
    // вызывается внутри транзакции inbox, блокировка держится до коммита
    public static async Task<CheckState?> GetStateForUpdateAsync(
        this IncidentsDbContext dbContext,
        Guid checkId,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlAsync(
            $"select 1 from check_states where check_id = {checkId} for update",
            cancellationToken);

        return await dbContext.CheckStates
            .Include(x => x.OpenIncident)
            .SingleOrDefaultAsync(x => x.CheckId == checkId, cancellationToken);
    }
}
