using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Recovery.ExternalState;

/// <summary>
/// Single entry point that re-derives run state from Git and GitHub, for startup recovery and periodic reconciliation
/// alike (every step is idempotent). Per repository the clone is fetched once; then every spec run past preparation is
/// reconciled step by step — local integration ref, ticket graph, finding issuances, worktrees, integration sagas, stack
/// bases — and finally merge tracking re-derives stack merge/closed status and resumes interrupted completions. A failing
/// step is reported and does not stop the other steps, specs, or repositories.
/// </summary>
public sealed class ExternalStateReconciler(
    IRepositoryRecordRepository repositories,
    ISpecRunRepository specRuns,
    IGitWorkspace git,
    IntegrationBranchReconciler integrationBranches,
    TicketGraphReconciler ticketGraphs,
    FindingIssuanceReconciler findingIssuances,
    WorktreeReconciler worktrees,
    IntegrationSagaReconciler integrationSagas,
    StackBaseReconciler stackBases,
    MergeTrackingService mergeTracking)
{
    private readonly ISpecReconciliationStep[] _steps =
        [integrationBranches, ticketGraphs, findingIssuances, worktrees, integrationSagas, stackBases];

    public async Task<ExternalReconciliationReport> ReconcileAsync(CancellationToken cancellationToken)
    {
        var actions = new List<ReconciliationAction>();
        var faults = new List<ReconciliationFault>();
        IEnumerable<IGrouping<int, SpecRun>> byRepository = (await specRuns.ListNonTerminalAsync(cancellationToken))
            .Where(HasExternalState)
            .OrderBy(spec => spec.RepositoryId)
            .ThenBy(spec => spec.QueuePosition)
            .GroupBy(spec => spec.RepositoryId);
        foreach (IGrouping<int, SpecRun> specs in byRepository)
        {
            await ReconcileRepositoryAsync(specs.Key, specs.Select(spec => spec.Id).ToArray(), actions, faults, cancellationToken);
        }

        MergeTrackingPass merges = await mergeTracking.TrackAllAsync(cancellationToken);
        return new ExternalReconciliationReport(actions, faults, merges);
    }

    /// <summary>Queued, waiting, and preparing runs have no Git/GitHub state of their own yet; the queue and preparation own them.</summary>
    private static bool HasExternalState(SpecRun spec) =>
        spec.Status is not (SpecRunStatus.Queued or SpecRunStatus.WaitingForDependency or SpecRunStatus.Preparing);

    private async Task ReconcileRepositoryAsync(
        int repositoryId,
        IReadOnlyList<RunId> specRunIds,
        List<ReconciliationAction> actions,
        List<ReconciliationFault> faults,
        CancellationToken cancellationToken)
    {
        RepositoryRecord? repository = await repositories.GetAsync(repositoryId, cancellationToken);
        if (repository is null)
        {
            return;
        }

        try
        {
            await git.FetchAsync(GitRepositoryLocation.From(repository), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            faults.Add(new ReconciliationFault(repositoryId, null, nameof(IGitWorkspace.FetchAsync), exception.Message));
            return;
        }

        foreach (RunId specRunId in specRunIds)
        {
            foreach (ISpecReconciliationStep step in _steps)
            {
                // Reloaded per step: an earlier step may have moved the spec (e.g. a resumed saga).
                if (await specRuns.GetAsync(specRunId, cancellationToken) is not { IsTerminal: false } spec)
                {
                    break;
                }

                try
                {
                    actions.AddRange(await step.ReconcileAsync(new SpecReconciliationContext(spec, repository), cancellationToken));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    faults.Add(new ReconciliationFault(repositoryId, specRunId, step.GetType().Name, exception.Message));
                }
            }
        }
    }
}
