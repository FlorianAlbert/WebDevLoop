using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Web.Components.Tickets;

/// <param name="Layer">0 for tickets without blockers, otherwise one more than the deepest blocker.</param>
/// <param name="IsFrontier">Blocked or Ready, with every blocker integrated: the ticket may be implemented now.</param>
public sealed record TicketDagRow(TicketRunView Ticket, int Layer, bool IsFrontier, IReadOnlyList<TicketBlocker> Blockers);

/// <param name="Issue">Null when the blocking ticket is not part of the loaded run.</param>
public sealed record TicketBlocker(string TicketRunId, int? Issue, TicketRunStatus? Status)
{
    public bool IsSatisfied => Status == TicketRunStatus.Integrated;
}
