namespace WebDevLoop.Core.Orchestration.TicketExecution;

public enum ImplementationOutcome
{
    /// <summary>The report was validated; the ticket moved to <c>Reviewing</c>.</summary>
    Implemented,
    /// <summary>The reported commit is not the ticket branch head; the ticket needs attention.</summary>
    UnexpectedCommitSha,
    /// <summary>
    /// Fix signal: the ticket branch does not contain the integration tip the implementer was given. The ticket needs
    /// attention and the step keeps its Copilot session id so a fix turn can resume the implementer.
    /// </summary>
    IntegrationMergeMissing,
    /// <summary>The worktree, prompt, or every allowed agent attempt failed; the ticket needs attention.</summary>
    Failed,
    /// <summary>The agent turn was cancelled (e.g. the ticket was aborted); the ticket is left to whoever cancelled it.</summary>
    Cancelled,
    /// <summary>Another runner already owns an active implement/fix step of the ticket; nothing was started.</summary>
    AlreadyRunning,
    /// <summary>The ticket is no longer <c>Implementing</c>; nothing was started.</summary>
    NotImplementing,
    /// <summary>A save lost a compare-and-swap race (duplicate runner or concurrent abort); the other writer wins.</summary>
    ConcurrencyConflict,
}

public sealed record ImplementationResult(ImplementationOutcome Outcome, string? Reason = null)
{
    public static ImplementationResult Implemented { get; } = new(ImplementationOutcome.Implemented);

    public static ImplementationResult AlreadyRunning { get; } = new(ImplementationOutcome.AlreadyRunning);

    public static ImplementationResult NotImplementing { get; } = new(ImplementationOutcome.NotImplementing);

    public static ImplementationResult ConcurrencyConflict { get; } = new(ImplementationOutcome.ConcurrencyConflict);
}
