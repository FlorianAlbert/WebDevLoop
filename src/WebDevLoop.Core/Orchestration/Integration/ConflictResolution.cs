using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Integration;

internal enum ConflictResolutionOutcome
{
    /// <summary>The ticket branch now contains the integration tip; <see cref="TicketRun.LastImplementedSha"/> was updated.</summary>
    Resolved,
    Failed,
    Cancelled,
    ConcurrencyConflict,
}

internal sealed record ConflictResolution(ConflictResolutionOutcome Outcome, string? Reason = null, AttentionReason? Attention = null)
{
    public static ConflictResolution Resolved { get; } = new(ConflictResolutionOutcome.Resolved);

    public static ConflictResolution ConcurrencyConflict { get; } = new(ConflictResolutionOutcome.ConcurrencyConflict);

    public static ConflictResolution Failed(string reason) => Failed(AttentionReasons.MergeConflictUnresolved(reason));

    public static ConflictResolution Failed(AttentionReason reason) => new(ConflictResolutionOutcome.Failed, reason.Details, reason);
}
