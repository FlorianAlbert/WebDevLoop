namespace WebDevLoop.Core.Orchestration.ReviewLoop;

internal enum FixOutcome
{
    /// <summary>The fix was validated; the ticket is back in <c>Reviewing</c> on the new head.</summary>
    Fixed,

    /// <summary>No implementer slot is free; the ticket stays <c>Reviewing</c>.</summary>
    NoImplementerCapacity,

    /// <summary>The fix failed; the ticket needs attention.</summary>
    Failed,

    Cancelled,
    ConcurrencyConflict,
}

internal sealed record FixResult(FixOutcome Outcome, string? Reason = null)
{
    public static FixResult Fixed { get; } = new(FixOutcome.Fixed);

    public static FixResult NoImplementerCapacity { get; } = new(FixOutcome.NoImplementerCapacity);

    public static FixResult ConcurrencyConflict { get; } = new(FixOutcome.ConcurrencyConflict);
}
