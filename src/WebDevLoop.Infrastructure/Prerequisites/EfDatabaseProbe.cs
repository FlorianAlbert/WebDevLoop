using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WebDevLoop.Infrastructure.Persistence;

namespace WebDevLoop.Infrastructure.Prerequisites;

/// <summary>
/// Opens the connection directly instead of <c>CanConnectAsync</c>, which reports a database that does not exist yet as
/// unreachable even though the migrations will create it.
/// </summary>
public sealed class EfDatabaseProbe(Func<WebDevLoopDbContext> contextFactory) : IDatabaseProbe
{
    public async Task<DatabaseState> InspectAsync(CancellationToken cancellationToken)
    {
        await using WebDevLoopDbContext context = contextFactory();
        try
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
        }
        catch (SqliteException)
        {
            return new DatabaseState(CanConnect: false, PendingMigrations: []);
        }

        IEnumerable<string> pending = await context.Database.GetPendingMigrationsAsync(cancellationToken);
        return new DatabaseState(CanConnect: true, PendingMigrations: pending.ToArray());
    }
}
