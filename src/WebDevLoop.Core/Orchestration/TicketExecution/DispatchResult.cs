using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.TicketExecution;

/// <param name="Dispatched">Tickets claimed into <see cref="TicketRunStatus.Implementing"/> and launched, in dispatch order.</param>
/// <param name="ConcurrencyConflict">A claim lost a compare-and-swap race; dispatch stopped and should be recomputed from fresh state.</param>
public sealed record DispatchResult(IReadOnlyList<TicketRunId> Dispatched, bool ConcurrencyConflict)
{
    public static DispatchResult Nothing { get; } = new([], false);
}
