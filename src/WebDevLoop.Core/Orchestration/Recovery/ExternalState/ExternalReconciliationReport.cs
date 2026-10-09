using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;

namespace WebDevLoop.Core.Orchestration.Recovery.ExternalState;

public enum ReconciliationActionKind
{
    /// <summary>The local run-scoped integration branch was missing or behind and now matches the pushed branch.</summary>
    IntegrationBranchRestored,
    /// <summary>A sub-issue the run had no ticket for (new, or reopened after it was closed outside the app) got a ticket run.</summary>
    TicketAdded,
    /// <summary>An unstarted ticket whose issue was closed or removed from the spec outside the app was skipped.</summary>
    TicketSkipped,
    /// <summary>The issue of an integrated ticket was reopened outside the app; recorded for the user, the ticket stays integrated.</summary>
    IntegratedTicketReopened,
    TicketDependencyAdded,
    TicketDependencyRemoved,
    /// <summary>A finding issue created on GitHub before a crash was recorded and its ticket run ensured.</summary>
    FindingIssuanceRecovered,
    /// <summary>A missing ticket worktree was recreated from the ticket branch.</summary>
    WorktreeRestored,
    /// <summary>A needs-attention ticket whose commit is already on the integration branch was moved back to integrating.</summary>
    ParkedIntegrationResumed,
    /// <summary>An unfinished integration saga was resumed from its last checkpoint.</summary>
    IntegrationResumed,
    /// <summary>A ticket whose saga has not squashed yet was handed to the background integration launcher.</summary>
    IntegrationLaunched,
    /// <summary>A pull request on the ticket's run-scoped stack branch does not carry this run's identifiers; it was not adopted.</summary>
    ForeignPullRequestRejected,
    /// <summary>The bottom PR of a stack-on-top spec was retargeted to trunk because the blocking stack below it merged.</summary>
    PullRequestBaseRetargeted,
}

/// <param name="Detail">Human-readable specifics (outcome, ids, refs).</param>
public sealed record ReconciliationAction(RunId SpecRunId, TicketRunId? TicketRunId, ReconciliationActionKind Kind, string Detail);

/// <param name="SpecRunId">Null when the whole repository could not be reconciled (e.g. the fetch failed).</param>
public sealed record ReconciliationFault(int RepositoryId, RunId? SpecRunId, string Step, string Error);

/// <param name="MergeTracking">The merge-tracking pass that ran after the per-spec reconciliation.</param>
public sealed record ExternalReconciliationReport(
    IReadOnlyList<ReconciliationAction> Actions,
    IReadOnlyList<ReconciliationFault> Faults,
    MergeTrackingPass MergeTracking);
