using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Attention;

/// <summary>What WebDevLoop sees of the ticket's git state when the troubleshooter is considered; the input of the prompt, the backups and the fingerprint.</summary>
/// <param name="Fingerprint">Identifies the situation: the reason code in the same git state gets the same fingerprint (the wording of the failure, which carries counters, is left out), so it is never escalated twice.</param>
public sealed record TroubleshootingState(
    string GitText,
    string GitHubText,
    string Fingerprint,
    WorktreeInspection Worktree,
    WorktreeChanges Changes,
    CommitSha? BranchTip,
    CommitSha? IntegrationTip);

/// <summary>Reads the git and GitHub state of a parked ticket with the ports WebDevLoop already has; every read is best effort and never mutates anything.</summary>
public sealed class TroubleshootingStateReader(IGitWorkspace git, IGitHubPullsAndStacks pulls, IPullStackLayerRepository layers)
{
    private const int ListedFiles = 30;
    private const int RecentCommits = 15;

    public async Task<TroubleshootingState> ReadAsync(AttentionWork work, AttentionCase attentionCase, CancellationToken cancellationToken)
    {
        TicketRun ticket = work.Ticket!;
        string path = work.TicketWorktreePath!;
        GitRepositoryLocation location = work.Location;
        WorktreeInspection worktree = await git.InspectWorktreeAsync(location, path, cancellationToken);
        WorktreeChanges changes = await git.GetWorktreeChangesAsync(location, path, cancellationToken);
        CommitSha? branchTip = await git.GetBranchTipAsync(location, ticket.BranchName, GitRefScope.Local, cancellationToken);
        CommitSha? remoteBranch = await git.GetBranchTipAsync(location, ticket.BranchName, GitRefScope.Remote, cancellationToken);
        CommitSha? integrationTip = await git.GetBranchTipAsync(location, work.Spec.IntegrationBranch, GitRefScope.Local, cancellationToken);
        CommitSha? remoteIntegration = await git.GetBranchTipAsync(location, work.Spec.IntegrationBranch, GitRefScope.Remote, cancellationToken);
        BranchName trunk = work.Spec.BaseBranch ?? work.Settings.BaseBranch;
        CommitSha? remoteTrunk = await git.GetBranchTipAsync(location, trunk, GitRefScope.Remote, cancellationToken);

        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Ticket worktree: {path}");
        text.AppendLine(CultureInfo.InvariantCulture, $"- status: {worktree.Status}; branch: {worktree.Branch?.Value ?? "(none)"}; HEAD: {Sha(worktree.Head)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"- tracked files with changes ({changes.TrackedFiles.Count}): {List(changes.TrackedFiles)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"- untracked files ({changes.UntrackedFiles.Count}): {List(changes.UntrackedFiles)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"- ignored files ({changes.IgnoredFiles.Count}): {List(changes.IgnoredFiles)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Ticket branch {ticket.BranchName}: local {Sha(branchTip)}, remote {Sha(remoteBranch)} (as of the last fetch)");
        text.AppendLine(CultureInfo.InvariantCulture, $"Last implemented (reviewed) commit: {Sha(ticket.LastImplementedSha)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Integration branch {work.Spec.IntegrationBranch}: local {Sha(integrationTip)}, remote {Sha(remoteIntegration)}; trunk {trunk} on the remote {Sha(remoteTrunk)}");
        if (branchTip is { } branch && integrationTip is { } integration)
        {
            bool based = await git.IsAncestorAsync(location, integration, branch, cancellationToken);
            CommitSha? mergeBase = await git.MergeBaseAsync(location, branch, integration, cancellationToken);
            text.AppendLine(CultureInfo.InvariantCulture, $"The ticket branch {(based ? "contains" : "does not contain")} the integration tip; merge base {Sha(mergeBase)}");
        }

        if (branchTip is { } recent)
        {
            text.AppendLine("Recent commits of the ticket branch:");
            foreach (string line in await git.GetRecentCommitsAsync(location, recent, RecentCommits, cancellationToken))
            {
                text.AppendLine("  " + line);
            }
        }

        string github = await ReadGitHubAsync(work, ticket, cancellationToken);
        string fingerprint = Fingerprint(attentionCase, ticket, worktree, changes, branchTip, integrationTip);
        return new TroubleshootingState(text.ToString().TrimEnd(), github, fingerprint, worktree, changes, branchTip, integrationTip);
    }

    private async Task<string> ReadGitHubAsync(AttentionWork work, TicketRun ticket, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        PullStackLayer[] own = [.. (await layers.ListBySpecRunAsync(work.Spec.Id, cancellationToken)).Where(layer => layer.TicketRunId == ticket.Id)];
        foreach (PullStackLayer layer in own)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- stack layer {layer.Position}: pull request #{layer.PullRequestNumber} for {layer.BranchName} onto {layer.BaseBranch} (stack {layer.StackNumber?.ToString(CultureInfo.InvariantCulture) ?? "none"})");
        }

        if (ticket.PullRequestNumber is { } number)
        {
            try
            {
                PullRequestSnapshot pull = await pulls.GetPullRequestAsync(work.Repository.Ref, number, cancellationToken);
                text.AppendLine(CultureInfo.InvariantCulture, $"- pull request #{number}: {pull.State}{(pull.IsDraft ? ", draft" : string.Empty)}, head {pull.Head} at {pull.HeadSha}, base {pull.Base}");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- pull request #{number}: could not be read ({exception.Message})");
            }
        }

        return text.Length == 0 ? "No pull request exists for this ticket yet." : text.ToString().TrimEnd();
    }

    private static string Fingerprint(
        AttentionCase attentionCase,
        TicketRun ticket,
        WorktreeInspection worktree,
        WorktreeChanges changes,
        CommitSha? branchTip,
        CommitSha? integrationTip)
    {
        string state = string.Join(
            '\n',
            attentionCase.Reason.Code,
            worktree.Status,
            worktree.Branch?.Value,
            Sha(worktree.Head),
            Sha(branchTip),
            Sha(integrationTip),
            Sha(ticket.LastImplementedSha),
            string.Join('|', changes.TrackedFiles.Order(StringComparer.Ordinal)),
            string.Join('|', changes.UntrackedFiles.Order(StringComparer.Ordinal)),
            string.Join('|', changes.IgnoredFiles.Order(StringComparer.Ordinal)));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(state)))[..32];
    }

    private static string Sha(CommitSha? sha) => sha?.Value ?? "(none)";

    private static string List(IReadOnlyList<string> files) =>
        files.Count == 0 ? "-" : string.Join(", ", files.Take(ListedFiles)) + (files.Count > ListedFiles ? $", … ({files.Count - ListedFiles} more)" : string.Empty);
}
