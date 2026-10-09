using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.TicketExecution;

/// <summary>
/// Free implementer slots derived from persisted ticket state: a ticket occupies a slot while an implementer works on it
/// (initial implementation or a review fix turn). Deriving it from the database keeps limits correct across retries,
/// crashes, and restarts. Call inside <see cref="ImplementerCapacityGate"/> together with the claim.
/// </summary>
public sealed class ImplementerCapacity(ISpecRunRepository specRuns, ITicketRunRepository ticketRuns)
{
    public static bool Occupies(TicketRunStatus status) =>
        status is TicketRunStatus.Implementing or TicketRunStatus.FixingReviewFindings;

    /// <returns>How many more implementers may start in <paramref name="repositoryId"/> right now (never negative).</returns>
    public async Task<int> GetAvailableAsync(int repositoryId, EffectiveSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        int occupiedGlobally = 0;
        int occupiedInRepository = 0;
        foreach (SpecRun specRun in await specRuns.ListNonTerminalAsync(cancellationToken))
        {
            IReadOnlyList<TicketRun> tickets = await ticketRuns.ListBySpecRunAsync(specRun.Id, cancellationToken);
            int occupied = tickets.Count(ticket => Occupies(ticket.Status));
            occupiedGlobally += occupied;
            if (specRun.RepositoryId == repositoryId)
            {
                occupiedInRepository += occupied;
            }
        }

        int available = Math.Min(
            settings.MaxConcurrentImplementersGlobal - occupiedGlobally,
            settings.MaxConcurrentImplementersPerRepo - occupiedInRepository);
        return Math.Max(0, available);
    }
}
