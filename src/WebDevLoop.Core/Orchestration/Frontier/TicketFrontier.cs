using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Frontier;

/// <summary>
/// Pure frontier calculation over a spec's ticket DAG: a ticket may be implemented once every ticket blocking it is
/// <see cref="TicketRunStatus.Integrated"/> or <see cref="TicketRunStatus.Skipped"/> by the user (see
/// <see cref="TicketRunStatusRules.SatisfiesDependents"/>). Blockers outside the snapshot never count as satisfied.
/// </summary>
public static class TicketFrontier
{
    public static FrontierSnapshot Compute(IReadOnlyCollection<TicketRun> tickets, IReadOnlyCollection<TicketDependency> dependencies)
    {
        ArgumentNullException.ThrowIfNull(tickets);
        ArgumentNullException.ThrowIfNull(dependencies);

        HashSet<TicketRunId> satisfied = tickets
            .Where(ticket => ticket.Status.SatisfiesDependents())
            .Select(ticket => ticket.Id)
            .ToHashSet();
        ILookup<TicketRunId, TicketRunId> blockers = dependencies.ToLookup(edge => edge.BlockedTicketRunId, edge => edge.BlockingTicketRunId);

        TicketRun[] free = tickets
            .Where(ticket => ticket.Status is TicketRunStatus.Blocked or TicketRunStatus.Ready)
            .Where(ticket => blockers[ticket.Id].All(satisfied.Contains))
            .OrderBy(ticket => ticket.Issue.Number)
            .ToArray();

        return new FrontierSnapshot(
            free.Where(ticket => ticket.Status == TicketRunStatus.Blocked).Select(ticket => ticket.Id).ToArray(),
            free.Select(ticket => ticket.Id).ToArray());
    }
}
