namespace WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;

public enum CompletionOutcome
{
    /// <summary>The stack was verified, every PR marked ready, and the spec moved on to <c>AwaitingMerge</c>.</summary>
    AwaitingMerge,

    /// <summary>No PRs exist: the integrated tickets were closed, the integration branch reported, and the spec completed.</summary>
    CompletedWithoutPullRequests,

    /// <summary>The worktrees of an aborted spec were cleaned up.</summary>
    CleanedUp,

    /// <summary>Stack verification (or publishing the integration branch) failed; the spec needs attention.</summary>
    NeedsAttention,

    /// <summary>A Git/GitHub call failed; the spec keeps its state so a later pass resumes it.</summary>
    Faulted,

    /// <summary>The spec has nothing to complete (not a passed <c>Testing</c> cycle, <c>ReadyForReview</c>, or aborted).</summary>
    NothingToDo,

    /// <summary>A save lost a compare-and-swap race; the other writer wins.</summary>
    ConcurrencyConflict,
}

public sealed record CompletionResult(CompletionOutcome Outcome, string? Reason = null)
{
    public static CompletionResult NothingToDo { get; } = new(CompletionOutcome.NothingToDo);

    public static CompletionResult ConcurrencyConflict { get; } = new(CompletionOutcome.ConcurrencyConflict);
}
