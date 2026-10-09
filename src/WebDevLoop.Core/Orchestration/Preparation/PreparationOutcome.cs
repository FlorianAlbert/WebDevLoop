namespace WebDevLoop.Core.Orchestration.Preparation;

public enum PreparationOutcome
{
    /// <summary>Spec and ticket DAG are snapshotted, the integration branch exists, and the run is <c>Running</c>.</summary>
    Prepared,

    /// <summary>Preparation hit a problem a human must resolve; the run is <c>NeedsAttention</c> with a reason.</summary>
    NeedsAttention,

    /// <summary>The run is not in <c>Preparing</c> (already prepared, parked, or finished); nothing was done.</summary>
    NotPreparing,

    /// <summary>Another writer changed the run first; the caller should retry with a fresh unit of work.</summary>
    ConcurrencyConflict,
}
