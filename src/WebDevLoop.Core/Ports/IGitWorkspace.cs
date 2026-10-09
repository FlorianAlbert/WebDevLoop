using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

/// <summary>
/// Local Git operations on app-owned clones and worktrees. Remote operations resolve fresh credentials per call
/// through <see cref="ITokenProvider"/>; paths must stay under the configured workspace root.
/// </summary>
public interface IGitWorkspace
{
    /// <summary>Clones when missing, otherwise fetches.</summary>
    Task EnsureClonedAsync(GitRepositoryLocation repo, CancellationToken cancellationToken);

    Task FetchAsync(GitRepositoryLocation repo, CancellationToken cancellationToken);

    Task<CommitSha?> GetBranchTipAsync(GitRepositoryLocation repo, BranchName branch, GitRefScope scope, CancellationToken cancellationToken);

    Task<bool> IsAncestorAsync(GitRepositoryLocation repo, CommitSha ancestor, CommitSha descendant, CancellationToken cancellationToken);

    /// <summary>Compare-and-swap on a local branch; a null <paramref name="expectedPriorTip"/> means the branch must not exist yet.</summary>
    Task<RefUpdateResult> UpdateBranchAsync(
        GitRepositoryLocation repo,
        BranchName branch,
        CommitSha newTip,
        CommitSha? expectedPriorTip,
        CancellationToken cancellationToken);

    Task<TicketWorktree> PrepareWorktreeAsync(GitRepositoryLocation repo, WorktreeSpec spec, CancellationToken cancellationToken);

    Task<WorktreeInspection> InspectWorktreeAsync(GitRepositoryLocation repo, string worktreePath, CancellationToken cancellationToken);

    /// <summary>Merges <paramref name="source"/> (normally the integration tip) into the worktree's branch.</summary>
    Task<GitMergeResult> MergeIntoWorktreeAsync(TicketWorktree worktree, CommitSha source, string message, CancellationToken cancellationToken);

    Task<GitMergeResult> CreateSquashCommitAsync(GitRepositoryLocation repo, SquashRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetChangedFilesAsync(GitRepositoryLocation repo, CommitSha from, CommitSha to, CancellationToken cancellationToken);

    Task<PushOutcome> PushAsync(GitRepositoryLocation repo, RefPush push, CancellationToken cancellationToken);

    /// <summary>Never deletes dirty or locked worktrees; those are retained with a warning.</summary>
    Task<WorktreeCleanupResult> CleanupWorktreeAsync(GitRepositoryLocation repo, string worktreePath, CancellationToken cancellationToken);
}
