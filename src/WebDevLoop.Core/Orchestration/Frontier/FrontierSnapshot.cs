using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Frontier;

/// <param name="Unblocked"><see cref="TicketRunStatus.Blocked"/> tickets whose current native blockers are all closed (to move to Ready).</param>
/// <param name="Dispatchable">
/// Ready or just unblocked tickets whose current native blockers are all closed, in dispatch order (lowest issue number first).
/// </param>
public sealed record FrontierSnapshot(IReadOnlyList<TicketRunId> Unblocked, IReadOnlyList<TicketRunId> Dispatchable)
{
    public static FrontierSnapshot Empty { get; } = new([], []);
}
