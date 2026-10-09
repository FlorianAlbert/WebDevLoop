namespace WebDevLoop.Core.Orchestration.Integration;

public enum IntegrationOutcome
{
    /// <summary>The ticket's stack layer is published and verified, its issue transitioned, and the ticket is <c>Integrated</c>.</summary>
    Integrated,

    /// <summary>
    /// The ticket is not <c>Integrating</c> or its spec is no longer active (e.g. aborted); the saga stopped before its next
    /// step and is left at its last checkpoint.
    /// </summary>
    NotIntegrating,

    /// <summary>A conflict-resolution step of the ticket is still active; nothing was done.</summary>
    AlreadyRunning,

    /// <summary>
    /// An earlier ticket of the same spec moved the integration branch but has not finished publishing its layer. The ticket
    /// stays <c>Integrating</c> and is launched again when that layer completes or the run is reconciled (see
    /// <see cref="IntegrationEventHandler"/>).
    /// </summary>
    WaitingForEarlierLayer,

    /// <summary>A saga step failed in a way retrying cannot fix (conflicts not resolved, moved refs, diff verification); the ticket needs attention.</summary>
    NeedsAttention,

    /// <summary>
    /// An unexpected error (for example a transient GitHub failure) interrupted the saga; it is recorded and the saga resumes
    /// on the next run (launched by reconciliation, see <see cref="IntegrationEventHandler"/>). More than <c>MaxRetries</c>
    /// faults in a row without progress yield <see cref="NeedsAttention"/> instead.
    /// </summary>
    Faulted,

    /// <summary>The conflict resolver was cancelled; the ticket is left to whoever cancelled it.</summary>
    Cancelled,

    /// <summary>A save lost a compare-and-swap race; the saga resumes from its last persisted checkpoint on the next run (see <see cref="Faulted"/>).</summary>
    ConcurrencyConflict,
}

public sealed record IntegrationResult(IntegrationOutcome Outcome, string? Reason = null)
{
    public static IntegrationResult Integrated { get; } = new(IntegrationOutcome.Integrated);

    public static IntegrationResult NotIntegrating { get; } = new(IntegrationOutcome.NotIntegrating);

    public static IntegrationResult AlreadyRunning { get; } = new(IntegrationOutcome.AlreadyRunning);

    public static IntegrationResult ConcurrencyConflict { get; } = new(IntegrationOutcome.ConcurrencyConflict);
}
