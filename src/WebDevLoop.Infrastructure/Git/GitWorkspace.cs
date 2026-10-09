using LibGit2Sharp;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Auth;

namespace WebDevLoop.Infrastructure.Git;

/// <summary>
/// <see cref="IGitWorkspace"/> over in-process LibGit2Sharp. Every path is confined to the workspace root and every
/// remote operation resolves its own credential callback.
/// </summary>
/// <remarks>
/// No <c>git</c> CLI fallback is needed for this port: clone/fetch/push, linked worktrees (add, lock state, prune),
/// in-memory merges and ref updates are all covered by LibGit2Sharp. A CLI fallback is only warranted for features
/// WebDevLoop does not use yet (sparse/partial clone, LFS, submodules).
/// </remarks>
public sealed class GitWorkspace : IGitWorkspace
{
    private readonly WorkspacePathGuard _paths;
    private readonly RepositoryLocks _locks = new();
    private readonly GitRemoteSync _remote;
    private readonly GitWorkspaceOptions _options;
    private readonly IClock _clock;

    public GitWorkspace(GitWorkspaceOptions options, IGitCredentialSource credentials, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(clock);
        _paths = new WorkspacePathGuard(options.WorkspaceRoot);
        _remote = new GitRemoteSync(credentials);
        _options = options;
        _clock = clock;
    }

    public Task EnsureClonedAsync(GitRepositoryLocation repo, CancellationToken cancellationToken)
    {
        string localPath = _paths.Confine(repo.LocalPath);
        return _locks.RunAsync(localPath, () =>
        {
            _remote.EnsureCloned(repo, localPath, cancellationToken);
            return true;
        }, cancellationToken);
    }

    public Task FetchAsync(GitRepositoryLocation repo, CancellationToken cancellationToken) =>
        WithRepositoryAsync(repo, git =>
        {
            _remote.Fetch(git, repo, cancellationToken);
            return true;
        }, cancellationToken);

    public Task<CommitSha?> GetBranchTipAsync(GitRepositoryLocation repo, BranchName branch, GitRefScope scope, CancellationToken cancellationToken) =>
        WithRepositoryAsync(repo, git => GitRefs.Tip(git, GitRefs.Canonical(branch, scope)), cancellationToken);

    public Task<bool> IsAncestorAsync(GitRepositoryLocation repo, CommitSha ancestor, CommitSha descendant, CancellationToken cancellationToken) =>
        WithRepositoryAsync(repo, git => GitRefs.IsAncestor(git, ancestor, descendant), cancellationToken);

    public Task<CommitSha?> MergeBaseAsync(GitRepositoryLocation repo, CommitSha first, CommitSha second, CancellationToken cancellationToken) =>
        WithRepositoryAsync(repo, git => GitRefs.MergeBase(git, first, second), cancellationToken);

    public Task<RefUpdateResult> UpdateBranchAsync(
        GitRepositoryLocation repo,
        BranchName branch,
        CommitSha newTip,
        CommitSha? expectedPriorTip,
        CancellationToken cancellationToken) =>
        WithRepositoryAsync(repo, git => GitRefs.CompareAndSwap(git, GitRefs.Canonical(branch), newTip, expectedPriorTip), cancellationToken);

    public Task<TicketWorktree> PrepareWorktreeAsync(GitRepositoryLocation repo, WorktreeSpec spec, CancellationToken cancellationToken)
    {
        string path = _paths.Confine(spec.Path);
        return WithRepositoryAsync(repo, git => GitWorktrees.Prepare(git, spec.Branch, spec.StartPoint, path), cancellationToken);
    }

    public Task<WorktreeInspection> InspectWorktreeAsync(GitRepositoryLocation repo, string worktreePath, CancellationToken cancellationToken)
    {
        string path = _paths.Confine(worktreePath);
        return WithRepositoryAsync(repo, git => GitWorktrees.Inspect(git, path), cancellationToken);
    }

    public Task<GitMergeResult> MergeIntoWorktreeAsync(TicketWorktree worktree, CommitSha source, string message, CancellationToken cancellationToken)
    {
        string path = _paths.Confine(worktree.Path);

        // Keyed by worktree: the port carries no clone location, and a merge only touches this worktree's index and branch.
        return _locks.RunAsync(path, () =>
        {
            using var linked = new Repository(path);
            return GitMerges.MergeIntoWorktree(linked, worktree.Branch, source, message, Signature());
        }, cancellationToken);
    }

    public Task<GitMergeResult> CreateSquashCommitAsync(GitRepositoryLocation repo, SquashRequest request, CancellationToken cancellationToken) =>
        WithRepositoryAsync(repo, git => GitMerges.Squash(git, request, Signature()), cancellationToken);

    public Task<IReadOnlyList<string>> GetChangedFilesAsync(GitRepositoryLocation repo, CommitSha from, CommitSha to, CancellationToken cancellationToken) =>
        WithRepositoryAsync(repo, git => GitRefs.ChangedFiles(git, from, to), cancellationToken);

    public Task<PushOutcome> PushAsync(GitRepositoryLocation repo, RefPush push, CancellationToken cancellationToken) =>
        WithRepositoryAsync(repo, git => _remote.Push(git, repo, push, cancellationToken), cancellationToken);

    public Task<WorktreeCleanupResult> CleanupWorktreeAsync(GitRepositoryLocation repo, string worktreePath, CancellationToken cancellationToken)
    {
        string path = _paths.Confine(worktreePath);
        return WithRepositoryAsync(repo, git => GitWorktrees.Cleanup(git, path), cancellationToken);
    }

    private Signature Signature() => new(_options.CommitterName, _options.CommitterEmail, _clock.UtcNow);

    private Task<T> WithRepositoryAsync<T>(GitRepositoryLocation repo, Func<Repository, T> operation, CancellationToken cancellationToken)
    {
        string localPath = _paths.Confine(repo.LocalPath);
        return _locks.RunAsync(localPath, () =>
        {
            using var git = new Repository(localPath);
            return operation(git);
        }, cancellationToken);
    }
}
