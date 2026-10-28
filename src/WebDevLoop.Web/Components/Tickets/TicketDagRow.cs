using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Web.Components.Tickets;

/// <param name="Layer">0 for tickets without blockers, otherwise one more than the deepest blocker.</param>
/// <param name="IsFrontier">Ready after the orchestrator's last check; dispatch rechecks current native GitHub blockers.</param>
public sealed record TicketDagRow(TicketRunView Ticket, int Layer, bool IsFrontier, IReadOnlyList<TicketBlocker> Blockers);

/// <param name="Issue">Null when the blocking ticket is not part of the loaded run.</param>
public sealed record TicketBlocker(string TicketRunId, int? Issue, TicketRunStatus? Status)
{
    /// <summary>Display-only local completion, not the current native GitHub blocker state.</summary>
    public bool IsLocallyComplete => Status?.IsLocallyComplete() == true;
}
