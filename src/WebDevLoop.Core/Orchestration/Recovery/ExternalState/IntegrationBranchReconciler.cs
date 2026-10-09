using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Recovery.ExternalState;

/// <summary>
/// Makes the local run-scoped integration branch usable again when the clone lost it or fell behind the pushed branch (e.g.
/// the clone was recreated): sagas compare-and-swap the local ref against the recorded tip, so a missing or stale local ref
/// would otherwise look like a moved branch. A local branch ahead of (or diverged from) the remote is never touched; the
/// saga that moved it pushes it.
/// </summary>
public sealed class IntegrationBranchReconciler(IGitWorkspace git) : ISpecReconciliationStep
{
    public async Task<IReadOnlyList<ReconciliationAction>> ReconcileAsync(SpecReconciliationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        SpecRun spec = context.Spec;
        GitRepositoryLocation location = context.Location;
        if (spec.IntegrationTipSha is null
            || await git.GetBranchTipAsync(location, spec.IntegrationBranch, GitRefScope.Remote, cancellationToken) is not { } remote)
        {
            return [];
        }

        CommitSha? local = await git.GetBranchTipAsync(location, spec.IntegrationBranch, GitRefScope.Local, cancellationToken);
        if (local == remote || (local is { } behind && !await git.IsAncestorAsync(location, behind, remote, cancellationToken)))
        {
            return [];
        }

        RefUpdateResult update = await git.UpdateBranchAsync(location, spec.IntegrationBranch, remote, local, cancellationToken);
        return update.Succeeded
            ? [new ReconciliationAction(spec.Id, null, ReconciliationActionKind.IntegrationBranchRestored, $"'{spec.IntegrationBranch}' {local?.Value ?? "(missing)"} -> {remote}")]
            : [];
    }
}
