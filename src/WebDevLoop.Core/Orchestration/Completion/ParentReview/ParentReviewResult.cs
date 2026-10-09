using WebDevLoop.Core.Orchestration.Findings;

namespace WebDevLoop.Core.Orchestration.Completion.ParentReview;

public enum ParentReviewOutcome
{
    /// <summary>Both axes reported clean; the spec moved to <c>Testing</c>.</summary>
    ReadyForTesting,

    /// <summary>Findings became finding tickets and the spec moved back to <c>Running</c> to work them.</summary>
    FindingTicketsCreated,

    /// <summary>
    /// The review still found issues in the last allowed cycle (<c>ParentReviewCycleLimit</c>). Finding tickets were
    /// created, but the spec needs attention instead of looping again.
    /// </summary>
    CycleLimitReached,

    /// <summary>Every finding repeats one whose ticket is already done, so working tickets cannot fix it; the spec needs attention.</summary>
    NoNewWork,

    /// <summary>The review could not run or an axis failed every attempt; the spec needs attention.</summary>
    Failed,

    /// <summary>A reviewer turn was cancelled (e.g. the run was aborted); the spec is left to whoever cancelled it.</summary>
    Cancelled,

    /// <summary>Another runner is already reviewing this spec; nothing was started.</summary>
    AlreadyRunning,

    /// <summary>The spec is not <c>ParentReviewing</c>; nothing was started.</summary>
    NotParentReviewing,

    /// <summary>A save lost a compare-and-swap race (duplicate runner or concurrent abort); the other writer wins.</summary>
    ConcurrencyConflict,
}

/// <param name="Tickets">The finding tickets of this cycle (new or reused); empty unless findings were issued.</param>
public sealed record ParentReviewResult(ParentReviewOutcome Outcome, IReadOnlyList<FindingTicket> Tickets, string? Reason = null)
{
    public static ParentReviewResult AlreadyRunning { get; } = new(ParentReviewOutcome.AlreadyRunning, []);

    public static ParentReviewResult NotParentReviewing { get; } = new(ParentReviewOutcome.NotParentReviewing, []);

    public static ParentReviewResult ConcurrencyConflict { get; } = new(ParentReviewOutcome.ConcurrencyConflict, []);
}
