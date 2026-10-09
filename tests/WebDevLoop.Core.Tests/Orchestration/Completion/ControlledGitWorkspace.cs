using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Orchestration.Completion;

/// <summary>
/// Passes every call to the in-memory Git. After <see cref="PauseAtNextBranchTip"/>, the next branch-tip lookup (made right
/// after a runner's status checks) waits until <see cref="Resume"/>, so a duplicate runner can be interleaved with a live
/// one at that point; <see cref="FailNextPrepare"/> makes the next checkout fail.
/// </summary>
internal sealed class ControlledGitWorkspace(IGitWorkspace inner) : IGitWorkspace
{
    private TaskCompletionSource? _armed;
    private TaskCompletionSource? _paused;

    public Exception? FailNextPrepare { get; set; }

    public bool IsPaused => _paused is { Task.IsCompleted: false };

    public void PauseAtNextBranchTip() => _armed = new TaskCompletionSource();

    public void Resume() => _paused!.SetResult();

    public Task EnsureClonedAsync(GitRepositoryLocation repo, CancellationToken cancellationToken) => inner.EnsureClonedAsync(repo, cancellationToken);

    public Task FetchAsync(GitRepositoryLocation repo, CancellationToken cancellationToken) => inner.FetchAsync(repo, cancellationToken);

    public async Task<CommitSha?> GetBranchTipAsync(GitRepositoryLocation repo, BranchName branch, GitRefScope scope, CancellationToken cancellationToken)
    {
        if (_armed is { } pause)
        {
            (_armed, _paused) = (null, pause);
            await pause.Task.ConfigureAwait(false);
        }

        return await inner.GetBranchTipAsync(repo, branch, scope, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> IsAncestorAsync(GitRepositoryLocation repo, CommitSha ancestor, CommitSha descendant, CancellationToken cancellationToken) =>
        inner.IsAncestorAsync(repo, ancestor, descendant, cancellationToken);

    public Task<CommitSha?> MergeBaseAsync(GitRepositoryLocation repo, CommitSha first, CommitSha second, CancellationToken cancellationToken) =>
        inner.MergeBaseAsync(repo, first, second, cancellationToken);

    public Task<RefUpdateResult> UpdateBranchAsync(
        GitRepositoryLocation repo,
        BranchName branch,
        CommitSha newTip,
        CommitSha? expectedPriorTip,
        CancellationToken cancellationToken) =>
        inner.UpdateBranchAsync(repo, branch, newTip, expectedPriorTip, cancellationToken);

    public Task<TicketWorktree> PrepareWorktreeAsync(GitRepositoryLocation repo, WorktreeSpec spec, CancellationToken cancellationToken)
    {
        if (FailNextPrepare is { } failure)
        {
            FailNextPrepare = null;
            return Task.FromException<TicketWorktree>(failure);
        }

        return inner.PrepareWorktreeAsync(repo, spec, cancellationToken);
    }

    public Task<WorktreeInspection> InspectWorktreeAsync(GitRepositoryLocation repo, string worktreePath, CancellationToken cancellationToken) =>
        inner.InspectWorktreeAsync(repo, worktreePath, cancellationToken);

    public Task<GitMergeResult> MergeIntoWorktreeAsync(TicketWorktree worktree, CommitSha source, string message, CancellationToken cancellationToken) =>
        inner.MergeIntoWorktreeAsync(worktree, source, message, cancellationToken);

    public Task<GitMergeResult> CreateSquashCommitAsync(GitRepositoryLocation repo, SquashRequest request, CancellationToken cancellationToken) =>
        inner.CreateSquashCommitAsync(repo, request, cancellationToken);

    public Task<IReadOnlyList<string>> GetChangedFilesAsync(GitRepositoryLocation repo, CommitSha from, CommitSha to, CancellationToken cancellationToken) =>
        inner.GetChangedFilesAsync(repo, from, to, cancellationToken);

    public Task<PushOutcome> PushAsync(GitRepositoryLocation repo, RefPush push, CancellationToken cancellationToken) =>
        inner.PushAsync(repo, push, cancellationToken);

    public Task<WorktreeCleanupResult> CleanupWorktreeAsync(GitRepositoryLocation repo, string worktreePath, CancellationToken cancellationToken) =>
        inner.CleanupWorktreeAsync(repo, worktreePath, cancellationToken);
}
