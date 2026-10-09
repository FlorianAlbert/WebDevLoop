using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.Preparation;

/// <summary>
/// Prepares a claimed spec run (entry point for the preparation worker; call for runs that entered
/// <see cref="SpecRunStatus.Preparing"/>): clone/fetch, snapshot the ticket DAG (step 1), choose the integration base and
/// create the run-scoped integration branch (step 3), optionally explore (step 2, against the new integration tip), then
/// move the run to <see cref="SpecRunStatus.Running"/> and request a frontier computation. Safe to call again after a
/// crash or a retry: the snapshot and the recorded base SHA are reused, and branch creation is replay-safe.
/// </summary>
public sealed class SpecPreparationService(
    IRepositoryRecordRepository repositories,
    ISpecRunRepository specRuns,
    IEffectiveSettingsProvider settings,
    IGitWorkspace git,
    SpecSnapshotter snapshotter,
    SpecExplorer explorer,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock,
    SpecPreparationOptions options)
{
    public async Task<PreparationOutcome> PrepareAsync(RunId specRunId, CancellationToken cancellationToken)
    {
        SpecRun? run = await specRuns.GetAsync(specRunId, cancellationToken);
        if (run is not { Status: SpecRunStatus.Preparing })
        {
            return PreparationOutcome.NotPreparing;
        }

        RepositoryRecord? repository = await repositories.GetAsync(run.RepositoryId, cancellationToken);
        if (repository is null)
        {
            return await NeedsAttentionAsync(run, $"Repository {run.RepositoryId} is no longer registered.", cancellationToken);
        }

        EffectiveSettings effective = await settings.GetAsync(run.RepositoryId, cancellationToken);
        var location = GitRepositoryLocation.From(repository);
        await git.EnsureClonedAsync(location, cancellationToken);

        if (await snapshotter.SnapshotAsync(run, cancellationToken) is { } snapshotFailure)
        {
            return await NeedsAttentionAsync(run, snapshotFailure, cancellationToken);
        }

        if (await ChooseIntegrationBaseAsync(run, location, effective, cancellationToken) is { } baseFailure)
        {
            return await NeedsAttentionAsync(run, baseFailure, cancellationToken);
        }

        // Persist the snapshot and chosen base before any ref is created, so a replay reuses the same base.
        if (await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.ConcurrencyConflict)
        {
            return PreparationOutcome.ConcurrencyConflict;
        }

        if (await CreateIntegrationBranchAsync(run, location, cancellationToken) is { } branchFailure)
        {
            return await NeedsAttentionAsync(run, branchFailure, cancellationToken);
        }

        if (options.ExplorationEnabled)
        {
            ExplorationResult exploration = await explorer.ExploreAsync(run, repository, effective, cancellationToken);
            switch (exploration.Outcome)
            {
                case ExplorationOutcome.ConcurrencyConflict:
                    return PreparationOutcome.ConcurrencyConflict;
                case ExplorationOutcome.Failed:
                    return await NeedsAttentionAsync(run, exploration.FailureReason!, cancellationToken);
            }
        }

        DateTimeOffset now = clock.UtcNow;
        run.TransitionTo(SpecRunStatus.Running, now);
        outbox.Append(new SpecRunStatusChanged(run.Id, run.RepositoryId, SpecRunStatus.Preparing, SpecRunStatus.Running, now));
        outbox.Append(new FrontierReconciliationRequested(run.Id, now));
        return await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved
            ? PreparationOutcome.Prepared
            : PreparationOutcome.ConcurrencyConflict;
    }

    /// <summary>
    /// The scheduler presets the base for <see cref="SpecDependencyMode.StackOnTop"/> (the blocker's integration tip);
    /// otherwise the base is the current remote trunk tip.
    /// </summary>
    /// <returns>Null on success; otherwise why no base could be chosen.</returns>
    private async Task<string?> ChooseIntegrationBaseAsync(
        SpecRun run,
        GitRepositoryLocation location,
        EffectiveSettings effective,
        CancellationToken cancellationToken)
    {
        run.BaseBranch ??= effective.BaseBranch;
        if (run.IntegrationBaseSha is null)
        {
            CommitSha? trunkTip = await git.GetBranchTipAsync(location, run.BaseBranch.Value, GitRefScope.Remote, cancellationToken);
            if (trunkTip is null)
            {
                return $"Base branch '{run.BaseBranch}' does not exist on the remote of {location.Repo}.";
            }

            run.IntegrationBaseSha = trunkTip;
        }

        run.IntegrationTipSha ??= run.IntegrationBaseSha;
        return null;
    }

    /// <returns>Null on success; otherwise why the run-scoped integration branch could not be created.</returns>
    private async Task<string?> CreateIntegrationBranchAsync(SpecRun run, GitRepositoryLocation location, CancellationToken cancellationToken)
    {
        CommitSha baseSha = run.IntegrationBaseSha!.Value;
        RefUpdateResult created = await git.UpdateBranchAsync(location, run.IntegrationBranch, baseSha, expectedPriorTip: null, cancellationToken);
        return created.Succeeded
            ? null
            : $"Integration branch '{run.IntegrationBranch}' already exists at {created.ActualTip}, expected it to be created at {baseSha}.";
    }

    private async Task<PreparationOutcome> NeedsAttentionAsync(SpecRun run, string reason, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;
        SpecRunStatus previous = run.Status;
        run.MarkNeedsAttention(reason, now);
        outbox.Append(new SpecRunStatusChanged(run.Id, run.RepositoryId, previous, SpecRunStatus.NeedsAttention, now));
        return await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved
            ? PreparationOutcome.NeedsAttention
            : PreparationOutcome.ConcurrencyConflict;
    }
}
