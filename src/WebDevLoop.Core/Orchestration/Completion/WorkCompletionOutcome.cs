namespace WebDevLoop.Core.Orchestration.Completion;

/// <summary>The result of a running spec's current work-completion decision.</summary>
public enum WorkCompletionOutcome
{
    /// <summary>Integrated work is ready for scoped parent review; the spec moved to <c>ParentReviewing</c>.</summary>
    ReviewStarted,

    /// <summary>The spec is not <c>Running</c>; its state and reason were left unchanged.</summary>
    NotRunning,

    /// <summary>No eligible work remains and nothing was integrated; completed without an empty review or PR.</summary>
    NoEligibleWork,

    /// <summary>Selected work or current eligible tickets remain, including tickets with open blockers.</summary>
    TicketsOutstanding,

    /// <summary>Another writer changed persisted state first; recovery can retry the decision.</summary>
    ConcurrencyConflict,

    /// <summary>The parent opted out or closed; selected work finished and branches/PRs remain intact.</summary>
    Stopped,

    /// <summary>Required current GitHub information is unavailable or blockers defer progress; recovery retries.</summary>
    Deferred,

    /// <summary>The repository label is missing; new parent review waits for explicit label provisioning.</summary>
    RepositoryLabelMissing,

    /// <summary>The repository label lookup failed; new parent review waits for recovery.</summary>
    GitHubUnavailable,

    /// <summary>Every remaining eligible ticket has an open blocker and no selected ticket remains in flight; the slot was released.</summary>
    Parked,
}
