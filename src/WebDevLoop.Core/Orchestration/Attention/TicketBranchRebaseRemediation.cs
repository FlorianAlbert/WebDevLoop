using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Attention;

/// <summary>
/// A ticket branch that does not contain the integration tip gets the tip merged into it, so the work can be reviewed or
/// integrated against the current state. A merge that conflicts is undone and left to the user: choosing between two tickets'
/// changes is a decision, not a repair.
/// </summary>
public sealed class TicketBranchRebaseRemediation(AttentionWorkLoader loader, IGitWorkspace git) : IKnownRemediation
{
    public AttentionCode Code => AttentionCode.TicketBranchNotBasedOnIntegration;

    public int MaxAttempts => 1;

    public async Task<AttentionStageResult> TryAsync(AttentionCase attentionCase, int previousAttempts, CancellationToken cancellationToken)
    {
        if (await loader.LoadAsync(attentionCase, cancellationToken) is not { Ticket: { } ticket } work)
        {
            return AttentionStageResult.Unresolved("The ticket or its repository no longer exists.");
        }

        CommitSha? branchTip = await git.GetBranchTipAsync(work.Location, ticket.BranchName, GitRefScope.Local, cancellationToken);
        CommitSha? integrationTip = await git.GetBranchTipAsync(work.Location, work.Spec.IntegrationBranch, GitRefScope.Local, cancellationToken)
            ?? work.Spec.IntegrationTipSha;
        if (branchTip is not { } branch || integrationTip is not { } tip)
        {
            return AttentionStageResult.Unresolved("The ticket branch or the integration branch is missing locally.");
        }

        string path = work.TicketWorktreePath!;
        await git.PrepareWorktreeAsync(work.Location, new WorktreeSpec(ticket.BranchName, branch, path), cancellationToken);
        GitMergeResult merge = await git.MergeIntoWorktreeAsync(
            new TicketWorktree(path, ticket.BranchName, branch), tip, $"Merge integration branch into {ticket.BranchName}", cancellationToken);
        switch (merge.Outcome)
        {
            case GitMergeOutcome.Merged:
                ticket.LastImplementedSha = merge.Commit;
                return AttentionStageResult.Resolved(
                    $"Merged the integration branch ({tip}) into the ticket branch; the new commit {merge.Commit} is reviewed next.", AttentionResume.RetryAtReview);
            case GitMergeOutcome.AlreadyUpToDate:
                return AttentionStageResult.Resolved("The ticket branch already contains the integration branch; continuing.", AttentionResume.Retry);
            default:
                await git.CleanWorktreeAsync(work.Location, path, cancellationToken);
                return AttentionStageResult.Unresolved(
                    $"Merging the integration branch conflicts in {string.Join(", ", merge.ConflictedPaths)}.",
                    $"Merged the integration branch into the ticket branch: conflicts in {string.Join(", ", merge.ConflictedPaths)} (merge undone).");
        }
    }
}
