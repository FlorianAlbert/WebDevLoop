using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Frontier;

public enum FrontierOutcome
{
    /// <summary>The frontier was recomputed; unblocked tickets are Ready and dispatchable ones were claimed up to capacity.</summary>
    Reconciled,
    /// <summary>The spec run is not <see cref="SpecRunStatus.Running"/>; nothing was done.</summary>
    NotRunning,
    /// <summary>Compare-and-swap kept losing; periodic reconciliation will retry.</summary>
    ConcurrencyConflict,
}

/// <param name="Unblocked">Tickets moved from Blocked to Ready.</param>
/// <param name="Dispatched">Tickets claimed into Implementing and launched.</param>
public sealed record FrontierResult(FrontierOutcome Outcome, IReadOnlyList<TicketRunId> Unblocked, IReadOnlyList<TicketRunId> Dispatched)
{
    public static FrontierResult NotRunning { get; } = new(FrontierOutcome.NotRunning, [], []);
}
