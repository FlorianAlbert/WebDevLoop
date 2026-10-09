namespace WebDevLoop.Core.Ports;

/// <summary>
/// Atomically persists tracked repository changes and appended outbox events. Every modified versioned aggregate is
/// saved with compare-and-swap on its loaded <c>Version</c>; a lost race yields <see cref="SaveOutcome.ConcurrencyConflict"/>.
/// </summary>
public interface IUnitOfWork
{
    Task<SaveOutcome> SaveChangesAsync(CancellationToken cancellationToken);
}
