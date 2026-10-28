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
    /// <summary>The repository label is missing; new selections wait for explicit creation approval.</summary>
    RepositoryLabelMissing,
    /// <summary>A repository label or ticket could not be checked on GitHub; affected work was deferred.</summary>
    GitHubUnavailable,
}

/// <param name="Unblocked">Tickets moved from Blocked to Ready.</param>
/// <param name="Dispatched">Tickets claimed into Implementing and launched.</param>
public sealed record FrontierResult(FrontierOutcome Outcome, IReadOnlyList<TicketRunId> Unblocked, IReadOnlyList<TicketRunId> Dispatched)
{
    public IReadOnlyList<GitHubIssuesException> LookupErrors { get; init; } = [];

    public static FrontierResult NotRunning { get; } = new(FrontierOutcome.NotRunning, [], []);
}
