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
            // Retrying resumes the failed phase (see TicketRun.Retry); reconciliation also resumes a parked integration saga whose
            // commit already moved the integration branch (Integrating); skipping gives the ticket up.
            [TicketRunStatus.NeedsAttention] =
            [
                TicketRunStatus.Ready,
                TicketRunStatus.Reviewing,
                TicketRunStatus.Integrating,
                TicketRunStatus.Skipped,
            ],
            [TicketRunStatus.Integrated] = [],
            [TicketRunStatus.Skipped] = [],
            [TicketRunStatus.Aborted] = [],
        };

    public static bool IsTerminal(this TicketRunStatus status) =>
        status is TicketRunStatus.Integrated or TicketRunStatus.Skipped or TicketRunStatus.Aborted;

    /// <summary>
    /// Skip policy: a blocker the user skipped counts as done, so its dependents may start (on an integration branch without
    /// the skipped change). To hold dependents back instead, skip them too (cascade) or abort. An aborted blocker never
    /// satisfies its dependents.
    /// </summary>
    public static bool SatisfiesDependents(this TicketRunStatus status) =>
        status is TicketRunStatus.Integrated or TicketRunStatus.Skipped;

    /// <summary>An implementer works on the ticket: its initial implementation or a review fix turn.</summary>
    public static bool OccupiesImplementerSlot(this TicketRunStatus status) =>
        status is TicketRunStatus.Implementing or TicketRunStatus.FixingReviewFindings;

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
