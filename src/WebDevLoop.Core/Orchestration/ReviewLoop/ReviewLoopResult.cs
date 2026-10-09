namespace WebDevLoop.Core.Orchestration.ReviewLoop;

public enum ReviewLoopOutcome
{
    /// <summary>Both axes reported clean; the ticket moved to <c>Integrating</c>.</summary>
    Integrating,

    /// <summary>The last allowed review round still found issues; the ticket needs attention.</summary>
    ReviewIterationsExhausted,

    /// <summary>A review or fix turn failed (agent failures, invalid fix report, unusable worktree); the ticket needs attention.</summary>
    Failed,

    /// <summary>
    /// Findings must be fixed but no implementer slot is free. The ticket stays <c>Reviewing</c> with the persisted findings
    /// and the loop is launched again when a slot frees or the run is reconciled (see <see cref="ReviewLoopEventHandler"/>).
    /// </summary>
    AwaitingImplementerCapacity,

    /// <summary>An agent turn was cancelled (e.g. the ticket was aborted); the ticket is left to whoever cancelled it.</summary>
    Cancelled,

    /// <summary>Another loop is already reviewing or fixing the ticket; nothing was started.</summary>
    AlreadyRunning,

    /// <summary>The ticket is not <c>Reviewing</c>; nothing was started.</summary>
    NotReviewing,

    /// <summary>A save lost a compare-and-swap race (duplicate loop or concurrent abort); the other writer wins.</summary>
    ConcurrencyConflict,
}

public sealed record ReviewLoopResult(ReviewLoopOutcome Outcome, string? Reason = null)
{
    public static ReviewLoopResult Integrating { get; } = new(ReviewLoopOutcome.Integrating);

    public static ReviewLoopResult AwaitingImplementerCapacity { get; } = new(ReviewLoopOutcome.AwaitingImplementerCapacity);

    public static ReviewLoopResult AlreadyRunning { get; } = new(ReviewLoopOutcome.AlreadyRunning);

    public static ReviewLoopResult NotReviewing { get; } = new(ReviewLoopOutcome.NotReviewing);

    public static ReviewLoopResult ConcurrencyConflict { get; } = new(ReviewLoopOutcome.ConcurrencyConflict);
}
