using System.Text.Json;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Recovery.ExternalState;

/// <summary>
/// Keeps the bottom PR of a stack-on-top spec mergeable once the blocking stack below it merged. The bottom layer was
/// published onto the blocking spec's top stack branch; after a human merged that PR (typically as a squash), the branch is
/// stale or deleted and the PR must target trunk, so it is retargeted there (GitHub may already have done so). The layer's
/// recorded base keeps naming the blocking stack branch: stack verification uses it to confirm that the stack below merged.
/// Only verified bottom layers are touched, so an in-flight integration saga never sees its PR base change.
/// </summary>
public sealed class StackBaseReconciler(
    IPullStackLayerRepository layers,
    IGitHubPullsAndStacks pulls,
    IRunEventRepository runEvents,
    IUnitOfWork unitOfWork,
    IClock clock) : ISpecReconciliationStep
{
    public async Task<IReadOnlyList<ReconciliationAction>> ReconcileAsync(SpecReconciliationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        SpecRun spec = context.Spec;
        BranchName trunk = context.Trunk;
        if (spec.DependencyModeUsed != SpecDependencyMode.StackOnTop
            || (await layers.ListBySpecRunAsync(spec.Id, cancellationToken)).FirstOrDefault() is not { } bottom
            || bottom.BaseBranch == trunk
            || bottom.VerifiedDiffSha != bottom.CommitSha)
        {
            return [];
        }

        GitHubRepoRef repository = context.Repository.Ref;
        PullRequestSnapshot pull = await pulls.GetPullRequestAsync(repository, bottom.PullRequestNumber, cancellationToken);
        if (pull.State != PullRequestState.Open
            || pull.Base == trunk
            || await pulls.FindPullRequestByHeadAsync(repository, bottom.BaseBranch, cancellationToken) is not { State: PullRequestState.Merged } below)
        {
            return [];
        }

        await pulls.UpdatePullRequestBaseAsync(repository, bottom.PullRequestNumber, trunk, cancellationToken);
        string payload = JsonSerializer.Serialize(new
        {
            pullRequest = bottom.PullRequestNumber.Value,
            from = pull.Base.Value,
            to = trunk.Value,
            mergedBelow = below.Number.Value,
        });
        runEvents.Add(RunEvent.Create(spec.Id, bottom.TicketRunId, ExternalStateRunEvents.PullRequestBaseRetargeted, payload, clock.UtcNow));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return
        [
            new ReconciliationAction(
                spec.Id,
                bottom.TicketRunId,
                ReconciliationActionKind.PullRequestBaseRetargeted,
                $"#{bottom.PullRequestNumber} retargeted from '{pull.Base}' to '{trunk}' after #{below.Number} merged."),
        ];
    }
}
