using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Attention;

/// <param name="Resume">How to continue when <paramref name="Verified"/>; null otherwise.</param>
public sealed record TroubleshooterVerdict(bool Verified, string Explanation, AttentionResume? Resume = null);

/// <summary>
/// Checks a troubleshooter's claim that the problem is solved against the repository instead of trusting the agent: the check
/// that failed is run again by WebDevLoop (see <see cref="TroubleshooterCheck"/>). A session that destroyed commits of the ticket
/// branch is undone here, whatever it claimed.
/// </summary>
public sealed class TroubleshooterVerifier(IGitWorkspace git)
{
    public async Task<TroubleshooterVerdict> VerifyAsync(
        AttentionWork work,
        TroubleshooterCheck check,
        TroubleshootingState before,
        CancellationToken cancellationToken)
    {
        TicketRun ticket = work.Ticket!;
        string path = work.TicketWorktreePath!;
        GitRepositoryLocation location = work.Location;
        CommitSha? tip = await git.GetBranchTipAsync(location, ticket.BranchName, GitRefScope.Local, cancellationToken);

        if (before.BranchTip is { } previousTip && tip is { } currentTip && !await git.IsAncestorAsync(location, previousTip, currentTip, cancellationToken))
        {
            await RestoreBranchAsync(work, previousTip, currentTip, cancellationToken);
            return new TroubleshooterVerdict(false, $"The troubleshooter rewrote the ticket branch: its former tip {previousTip} was no longer part of it. The branch was restored to {previousTip}.");
        }

        WorktreeInspection worktree = await git.InspectWorktreeAsync(location, path, cancellationToken);
        if (worktree.Status != WorktreeStatus.Clean)
        {
            return new TroubleshooterVerdict(false, $"The ticket worktree is still {worktree.Status}, not a clean checkout.");
        }

        if (worktree.Branch != ticket.BranchName)
        {
            return new TroubleshooterVerdict(false, $"The ticket worktree is on '{worktree.Branch?.Value ?? "(no branch)"}' instead of '{ticket.BranchName}'.");
        }

        if (tip is not { } branchTip || worktree.Head != branchTip)
        {
            return new TroubleshooterVerdict(false, $"The worktree HEAD ({worktree.Head?.Value ?? "(missing)"}) is not the tip of '{ticket.BranchName}' ({tip?.Value ?? "(missing)"}).");
        }

        if (ticket.LastImplementedSha is { } reviewed && !await git.IsAncestorAsync(location, reviewed, branchTip, cancellationToken))
        {
            return new TroubleshooterVerdict(false, $"The reviewed commit {reviewed} is no longer part of the ticket branch.");
        }

        bool moved = before.BranchTip != branchTip;
        CommitSha? integrationTip = await git.GetBranchTipAsync(location, work.Spec.IntegrationBranch, GitRefScope.Local, cancellationToken) ?? before.IntegrationTip;
        if ((moved || check == TroubleshooterCheck.TicketBasedOnIntegration)
            && integrationTip is { } integration
            && !await git.IsAncestorAsync(location, integration, branchTip, cancellationToken))
        {
            return new TroubleshooterVerdict(false, $"The ticket branch at {branchTip} does not contain the integration tip {integration}.");
        }

        if (!moved)
        {
            return new TroubleshooterVerdict(true, $"The ticket worktree is a clean checkout of '{ticket.BranchName}' at {branchTip}.", AttentionResume.Retry);
        }

        // New commits were never reviewed, so the ticket continues in review rather than in the phase that failed.
        ticket.LastImplementedSha = branchTip;
        return new TroubleshooterVerdict(
            true, $"The ticket worktree is a clean checkout of '{ticket.BranchName}' at the new commit {branchTip}, which is reviewed next.", AttentionResume.RetryAtReview);
    }

    private async Task RestoreBranchAsync(AttentionWork work, CommitSha previousTip, CommitSha currentTip, CancellationToken cancellationToken)
    {
        await git.UpdateBranchAsync(work.Location, work.Ticket!.BranchName, previousTip, currentTip, cancellationToken);
        await git.CleanWorktreeAsync(work.Location, work.TicketWorktreePath!, cancellationToken);
    }
}
