using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

public interface ITicketRunRepository
{
    Task<TicketRun?> GetAsync(TicketRunId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<TicketRun>> ListBySpecRunAsync(RunId specRunId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TicketDependency>> ListDependenciesAsync(RunId specRunId, CancellationToken cancellationToken);

    /// <summary>
    /// Tickets of non-terminal spec runs that occupy an implementer slot (see <see cref="TicketRunStatusRules.OccupiesImplementerSlot"/>),
    /// counted from committed rows without tracking, so a long-lived unit of work never sees its own stale copies.
    /// </summary>
    Task<ImplementerSlotUsage> CountOccupiedImplementerSlotsAsync(int repositoryId, CancellationToken cancellationToken);

    /// <summary>
    /// The committed status of a ticket, read without tracking, so a long-lived unit of work sees changes other scopes
    /// committed (e.g. an abort); null when the ticket does not exist.
    /// </summary>
    Task<TicketRunStatus?> GetCommittedStatusAsync(TicketRunId id, CancellationToken cancellationToken);

    void Add(TicketRun ticketRun);

    void AddDependency(TicketDependency dependency);

    /// <summary>Drops an edge that no longer exists on GitHub (reconciliation of the ticket graph).</summary>
    void RemoveDependency(TicketDependency dependency);
}
