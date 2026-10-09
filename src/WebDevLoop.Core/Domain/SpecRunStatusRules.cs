namespace WebDevLoop.Core.Domain;

public static class SpecRunStatusRules
{
    private static readonly IReadOnlyDictionary<SpecRunStatus, SpecRunStatus[]> Transitions =
        new Dictionary<SpecRunStatus, SpecRunStatus[]>
        {
            [SpecRunStatus.Queued] = [SpecRunStatus.WaitingForDependency, SpecRunStatus.Preparing],
            [SpecRunStatus.WaitingForDependency] = [SpecRunStatus.Preparing],
            [SpecRunStatus.Preparing] = [SpecRunStatus.Running],
            [SpecRunStatus.Running] = [SpecRunStatus.ParentReviewing],
            [SpecRunStatus.ParentReviewing] = [SpecRunStatus.Running, SpecRunStatus.Testing],
            // Testing -> Completed is the no-PR completion path.
            [SpecRunStatus.Testing] = [SpecRunStatus.Running, SpecRunStatus.ReadyForReview, SpecRunStatus.Completed],
            [SpecRunStatus.ReadyForReview] = [SpecRunStatus.AwaitingMerge],
            [SpecRunStatus.AwaitingMerge] = [SpecRunStatus.Completed],
            // Retrying a spec resumes it at the phase that exhausted its cycle limit.
            [SpecRunStatus.NeedsAttention] =
            [
                SpecRunStatus.Preparing,
                SpecRunStatus.Running,
                SpecRunStatus.ParentReviewing,
                SpecRunStatus.Testing,
            ],
            [SpecRunStatus.Completed] = [],
            [SpecRunStatus.Aborted] = [],
        };

    public static bool IsTerminal(this SpecRunStatus status) =>
        status is SpecRunStatus.Completed or SpecRunStatus.Aborted;

    /// <summary>Statuses that occupy a repo/dependency lane. <c>NeedsAttention</c> is parked, not active.</summary>
    public static bool IsActive(this SpecRunStatus status) =>
        status is SpecRunStatus.Preparing
            or SpecRunStatus.Running
            or SpecRunStatus.ParentReviewing
            or SpecRunStatus.Testing
            or SpecRunStatus.ReadyForReview
            or SpecRunStatus.AwaitingMerge;

    public static bool CanTransitionTo(this SpecRunStatus from, SpecRunStatus to)
    {
        if (from.IsTerminal())
        {
            return false;
        }

        if (to is SpecRunStatus.Aborted || (to is SpecRunStatus.NeedsAttention && from is not SpecRunStatus.NeedsAttention))
        {
            return true;
        }

        return Transitions[from].Contains(to);
    }
}
