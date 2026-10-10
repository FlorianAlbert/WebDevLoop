using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Preparation;

public enum ExplorationOutcome
{
    /// <summary>An explore step of the run has succeeded (now or earlier).</summary>
    Explored,

    /// <summary>Every attempt failed or exploration could not be set up; see the failure reason.</summary>
    Failed,

    ConcurrencyConflict,
}

public sealed record ExplorationResult(ExplorationOutcome Outcome, string? FailureReason = null, AttentionReason? Attention = null)
{
    public static ExplorationResult Explored { get; } = new(ExplorationOutcome.Explored);

    public static ExplorationResult Conflict { get; } = new(ExplorationOutcome.ConcurrencyConflict);

    public static ExplorationResult Failed(AttentionReason reason) => new(ExplorationOutcome.Failed, reason.Details, reason);
}
