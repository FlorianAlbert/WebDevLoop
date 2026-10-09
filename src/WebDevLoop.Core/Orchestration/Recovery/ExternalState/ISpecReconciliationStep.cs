using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Recovery.ExternalState;

/// <summary>A spec run plus its repository, as loaded at the start of one reconciliation step.</summary>
public sealed record SpecReconciliationContext(SpecRun Spec, RepositoryRecord Repository)
{
    public GitRepositoryLocation Location => GitRepositoryLocation.From(Repository);

    public BranchName Trunk => Spec.BaseBranch ?? Repository.DefaultBaseBranch;
}

/// <summary>
/// One idempotent aspect of re-deriving a spec run's state from Git and GitHub. Steps persist their own changes, so a step
/// that fails or loses a race leaves the others' work intact and the next pass simply repeats it.
/// </summary>
public interface ISpecReconciliationStep
{
    /// <returns>What the step changed; empty when the database already matched Git and GitHub.</returns>
    Task<IReadOnlyList<ReconciliationAction>> ReconcileAsync(SpecReconciliationContext context, CancellationToken cancellationToken);
}
