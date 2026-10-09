using Microsoft.EntityFrameworkCore;

namespace WebDevLoop.Infrastructure.Persistence;

public static class PersistenceDatabase
{
    /// <summary>Applies pending migrations and enables WAL so concurrent workers read while one writes (the setting persists in the file).</summary>
    public static async Task MigrateAsync(WebDevLoopDbContext context, CancellationToken cancellationToken)
    {
        await context.Database.MigrateAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
    }
}
