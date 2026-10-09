using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Persistence;

/// <summary>
/// Saves tracked changes and appended outbox rows in one transaction. Each modified aggregate is written with
/// compare-and-swap on the <c>Version</c> it was loaded with: the version is advanced before the UPDATE
/// (<c>SET Version = n + 1 WHERE Version = n</c>) and restored if the save does not succeed.
/// A version mismatch and a unique-index violation (lost claim race: active-spec slot, active step, finding fingerprint,
/// test lease, …) both yield <see cref="SaveOutcome.ConcurrencyConflict"/>; the change tracker is cleared so callers reload and re-evaluate.
/// </summary>
public sealed class EfUnitOfWork(WebDevLoopDbContext context) : IUnitOfWork
{
    private const int SqliteUniqueViolation = 2067;
    private const int SqlitePrimaryKeyViolation = 1555;

    public async Task<SaveOutcome> SaveChangesAsync(CancellationToken cancellationToken)
    {
        context.ChangeTracker.DetectChanges();
        List<EntityEntry<VersionedEntity>> advanced = context.ChangeTracker
            .Entries<VersionedEntity>()
            .Where(entry => entry.State == EntityState.Modified)
            .ToList();

        foreach (EntityEntry<VersionedEntity> entry in advanced)
        {
            entry.Entity.AdvanceVersion();
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return SaveOutcome.Saved;
        }
        catch (Exception exception) when (IsLostRace(exception))
        {
            RestoreVersions(advanced);
            context.ChangeTracker.Clear();
            return SaveOutcome.ConcurrencyConflict;
        }
        catch
        {
            RestoreVersions(advanced);
            throw;
        }
    }

    private static bool IsLostRace(Exception exception) =>
        exception is DbUpdateConcurrencyException
        || exception is DbUpdateException { InnerException: SqliteException { SqliteExtendedErrorCode: SqliteUniqueViolation or SqlitePrimaryKeyViolation } };

    private static void RestoreVersions(IEnumerable<EntityEntry<VersionedEntity>> entries)
    {
        foreach (EntityEntry<VersionedEntity> entry in entries)
        {
            PropertyEntry version = entry.Property(nameof(VersionedEntity.Version));
            version.CurrentValue = version.OriginalValue;
        }
    }
}
