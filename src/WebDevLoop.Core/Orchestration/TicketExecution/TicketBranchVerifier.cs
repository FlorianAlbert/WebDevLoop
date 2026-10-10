using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.TicketExecution;

/// <summary>
/// App-side Git checks around an implementer turn. Agent reports are claims; these checks compare them with the actual
/// ticket branch and the integration tip the agent was given.
/// </summary>
internal sealed class TicketBranchVerifier(IGitWorkspace git)
{
    /// <returns>Null when the worktree is clean, on the ticket branch, and based on <paramref name="integrationTip"/>; otherwise the problem.</returns>
    public async Task<AttentionReason?> VerifyWorktreeBaseAsync(
        GitRepositoryLocation location,
        string worktreePath,
        BranchName branch,
        CommitSha integrationTip,
        CancellationToken cancellationToken)
    {
        WorktreeInspection worktree = await git.InspectWorktreeAsync(location, worktreePath, cancellationToken);
        if (worktree.Status != WorktreeStatus.Clean || worktree.Branch != branch || worktree.Head is not { } head)
        {
            return AttentionReasons.WorktreeNotClean(
                worktreePath, branch.Value, $"Worktree '{worktreePath}' is {worktree.Status} on '{worktree.Branch}' instead of a clean checkout of '{branch}'.");
        }

        return await git.IsAncestorAsync(location, integrationTip, head, cancellationToken)
            ? null
            : AttentionReasons.TicketBranchNotBasedOnIntegration(
                branch.Value, $"Worktree '{worktreePath}' at {head} is not based on the integration tip {integrationTip}.");
    }

    public async Task<ReportVerification> VerifyReportAsync(
        GitRepositoryLocation location,
        string worktreePath,
        BranchName branch,
        CommitSha reportedHead,
        CommitSha integrationTip,
        CancellationToken cancellationToken)
    {
        CommitSha? branchTip = await git.GetBranchTipAsync(location, branch, GitRefScope.Local, cancellationToken);
        WorktreeInspection worktree = await git.InspectWorktreeAsync(location, worktreePath, cancellationToken);
        if (branchTip != reportedHead || worktree.Head != reportedHead)
        {
            string mismatch = $"Implementer reported {reportedHead}, but branch '{branch}' is at {branchTip?.Value ?? "(missing)"} "
                + $"and the worktree HEAD is {worktree.Head?.Value ?? "(missing)"}.";
            return new ReportVerification(ImplementationOutcome.UnexpectedCommitSha, mismatch, AttentionReasons.ReportedCommitMismatch(mismatch));
        }

        if (await git.IsAncestorAsync(location, integrationTip, reportedHead, cancellationToken))
        {
            return ReportVerification.Valid;
        }

        string missing = $"Branch '{branch}' at {reportedHead} does not contain the integration tip {integrationTip}; "
            + "merge the integration branch into the ticket branch before reporting.";
        return new ReportVerification(ImplementationOutcome.IntegrationMergeMissing, missing, AttentionReasons.TicketBranchNotBasedOnIntegration(branch.Value, missing));
    }
}

/// <param name="Outcome"><see cref="ImplementationOutcome.Implemented"/> when the report is valid; otherwise why not.</param>
internal sealed record ReportVerification(ImplementationOutcome Outcome, string? Reason, AttentionReason? Attention = null)
{
    public static ReportVerification Valid { get; } = new(ImplementationOutcome.Implemented, null);
}
