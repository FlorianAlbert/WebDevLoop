using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Frontier;

public enum FrontierOutcome
{
    /// <summary>The frontier was recomputed; unblocked tickets are Ready and dispatchable ones were claimed up to capacity.</summary>
    Reconciled,
    /// <summary>The spec run is not <see cref="SpecRunStatus.Running"/>; nothing was done.</summary>
    NotRunning,
    /// <summary>Compare-and-swap kept losing; periodic reconciliation will retry.</summary>
    ConcurrencyConflict,
    /// <summary>Some tickets could not be checked on GitHub and were deferred; unrelated tickets may have dispatched.</summary>
    GitHubUnavailable,
}

/// <param name="Unblocked">Tickets moved from Blocked to Ready.</param>
/// <param name="Dispatched">Tickets claimed into Implementing and launched.</param>
public sealed record FrontierResult(FrontierOutcome Outcome, IReadOnlyList<TicketRunId> Unblocked, IReadOnlyList<TicketRunId> Dispatched)
{
    public IReadOnlyList<GitHubIssuesException> LookupErrors { get; init; } = [];

    public static FrontierResult NotRunning { get; } = new(FrontierOutcome.NotRunning, [], []);
}
