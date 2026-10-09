namespace WebDevLoop.Core.Orchestration.Control;

/// <param name="Reason">Why the action was not applied.</param>
/// <param name="Warnings">Cleanup that failed after the action was applied (e.g. a session that could not be aborted).</param>
public sealed record ControlResult(ControlOutcome Outcome, string? Reason = null, IReadOnlyList<string>? Warnings = null)
{
    public bool IsApplied => Outcome == ControlOutcome.Applied;

    public static ControlResult Applied(IReadOnlyList<string>? warnings = null) => new(ControlOutcome.Applied, Warnings: warnings is { Count: > 0 } ? warnings : null);

    public static ControlResult NotFound(string reason) => new(ControlOutcome.NotFound, reason);

    public static ControlResult NotAllowed(string reason) => new(ControlOutcome.NotAllowed, reason);

    public static ControlResult ConcurrencyConflict(string reason) => new(ControlOutcome.ConcurrencyConflict, reason);
}
