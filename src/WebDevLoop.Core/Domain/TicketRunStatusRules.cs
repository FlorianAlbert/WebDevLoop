namespace WebDevLoop.Core.Domain;

public static class TicketRunStatusRules
{
    private static readonly IReadOnlyDictionary<TicketRunStatus, TicketRunStatus[]> Transitions =
        new Dictionary<TicketRunStatus, TicketRunStatus[]>
        {
            [TicketRunStatus.Blocked] = [TicketRunStatus.Ready, TicketRunStatus.Skipped],
            [TicketRunStatus.Ready] = [TicketRunStatus.Implementing, TicketRunStatus.Skipped],
            [TicketRunStatus.Implementing] = [TicketRunStatus.Reviewing],
            [TicketRunStatus.Reviewing] = [TicketRunStatus.FixingReviewFindings, TicketRunStatus.Integrating],
            [TicketRunStatus.FixingReviewFindings] = [TicketRunStatus.Reviewing],
            [TicketRunStatus.Integrating] = [TicketRunStatus.Integrated],
            [TicketRunStatus.NeedsAttention] = [TicketRunStatus.Ready, TicketRunStatus.Skipped],
            [TicketRunStatus.Integrated] = [],
            [TicketRunStatus.Skipped] = [],
            [TicketRunStatus.Aborted] = [],
        };

    public static bool IsTerminal(this TicketRunStatus status) =>
        status is TicketRunStatus.Integrated or TicketRunStatus.Skipped or TicketRunStatus.Aborted;

    public static bool CanTransitionTo(this TicketRunStatus from, TicketRunStatus to)
    {
        if (from.IsTerminal())
        {
            return false;
        }

        if (to is TicketRunStatus.Aborted || (to is TicketRunStatus.NeedsAttention && from is not TicketRunStatus.NeedsAttention))
        {
            return true;
        }

        return Transitions[from].Contains(to);
    }
}
