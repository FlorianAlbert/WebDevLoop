using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.TicketExecution;

/// <summary>
/// Free implementer slots derived from persisted ticket state: a ticket occupies a slot while an implementer works on it
/// (initial implementation or a review fix turn). Deriving it from the database keeps limits correct across retries,
/// crashes, and restarts; the count reads committed rows, so callers holding a long-lived unit of work (the review loop)
/// never count stale tracked tickets. Call inside <see cref="ImplementerCapacityGate"/> together with the claim.
/// </summary>
public sealed class ImplementerCapacity(ITicketRunRepository ticketRuns)
{
    public static bool Occupies(TicketRunStatus status) => status.OccupiesImplementerSlot();

    /// <returns>How many more implementers may start in <paramref name="repositoryId"/> right now (never negative).</returns>
    public async Task<int> GetAvailableAsync(int repositoryId, EffectiveSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ImplementerSlotUsage occupied = await ticketRuns.CountOccupiedImplementerSlotsAsync(repositoryId, cancellationToken);
        int available = Math.Min(
            settings.MaxConcurrentImplementersGlobal - occupied.Global,
            settings.MaxConcurrentImplementersPerRepo - occupied.InRepository);
        return Math.Max(0, available);
    }
}
