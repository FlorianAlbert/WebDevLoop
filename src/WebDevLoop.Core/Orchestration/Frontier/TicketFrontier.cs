using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Frontier;

/// <summary>
/// Orders unstarted tickets authorized by the current GitHub blocker check. Local ticket status and saved DAG edges
/// cannot authorize dispatch; missing authorization keeps a ticket out of the frontier.
/// </summary>
public static class TicketFrontier
{
    public static FrontierSnapshot Compute(IReadOnlyCollection<TicketRun> tickets, IReadOnlySet<TicketRunId> unblockedTickets)
    {
        ArgumentNullException.ThrowIfNull(tickets);
        ArgumentNullException.ThrowIfNull(unblockedTickets);

        TicketRun[] free = tickets
            .Where(ticket => ticket.Status is TicketRunStatus.Blocked or TicketRunStatus.Ready)
            .Where(ticket => unblockedTickets.Contains(ticket.Id))
            .OrderBy(ticket => ticket.Issue.Number)
            .ToArray();

        return new FrontierSnapshot(
            free.Where(ticket => ticket.Status == TicketRunStatus.Blocked).Select(ticket => ticket.Id).ToArray(),
            free.Select(ticket => ticket.Id).ToArray());
    }
}
