using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Ports.Fakes;

/// <summary>Commit graph plus local/remote refs and worktrees; commits record the files they touch.</summary>
public sealed class InMemoryGitWorkspace : IGitWorkspace
{
    private readonly Dictionary<CommitSha, (CommitSha[] Parents, string[] Files)> _commits = [];
    private readonly Dictionary<BranchName, CommitSha> _local = [];
    private readonly Dictionary<BranchName, CommitSha> _remote = [];
    private readonly Dictionary<string, (TicketWorktree Worktree, WorktreeStatus Status)> _worktrees = [];
    private int _nextCommit;

    public bool IsCloned { get; private set; }

    public IReadOnlyList<string>? ConflictOnNextSquash { get; set; }

    public CommitSha Commit(CommitSha[] parents, params string[] files)
    {
        var sha = new CommitSha((++_nextCommit).ToString("x40"));
        _commits[sha] = (parents, files);
        return sha;
    }

    public CommitSha SeedRemoteBranch(BranchName branch, params string[] files)
    {
        CommitSha sha = Commit([], files);
        _remote[branch] = sha;
        return sha;
    }

    public CommitSha CommitInWorktree(string path, params string[] files)
    {
        TicketWorktree worktree = _worktrees[path].Worktree;
        CommitSha sha = Commit([worktree.Head], files);
        _worktrees[path] = (worktree with { Head = sha }, WorktreeStatus.Clean);
        _local[worktree.Branch] = sha;
        return sha;
    }

    public void SetWorktreeStatus(string path, WorktreeStatus status) => _worktrees[path] = (_worktrees[path].Worktree, status);

    public CommitSha? RemoteTip(BranchName branch) => _remote.TryGetValue(branch, out CommitSha sha) ? sha : null;

    public Task EnsureClonedAsync(GitRepositoryLocation repo, CancellationToken cancellationToken)
    {
        IsCloned = true;
        return Task.CompletedTask;
    }

    public Task FetchAsync(GitRepositoryLocation repo, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<CommitSha?> GetBranchTipAsync(GitRepositoryLocation repo, BranchName branch, GitRefScope scope, CancellationToken cancellationToken)
    {
        Dictionary<BranchName, CommitSha> refs = scope == GitRefScope.Local ? _local : _remote;
        return Task.FromResult(refs.TryGetValue(branch, out CommitSha sha) ? sha : (CommitSha?)null);
    }

    public Task<bool> IsAncestorAsync(GitRepositoryLocation repo, CommitSha ancestor, CommitSha descendant, CancellationToken cancellationToken) =>
        Task.FromResult(Reachable(descendant).Contains(ancestor));

    public Task<RefUpdateResult> UpdateBranchAsync(
        GitRepositoryLocation repo,
        BranchName branch,
        CommitSha newTip,
        CommitSha? expectedPriorTip,
        CancellationToken cancellationToken)
    {
        CommitSha? current = _local.TryGetValue(branch, out CommitSha sha) ? sha : null;
        if (current == newTip)
        {
            return Task.FromResult(new RefUpdateResult(RefUpdateOutcome.AlreadyAtTarget, current));
        }

        if (current != expectedPriorTip)
        {
            return Task.FromResult(new RefUpdateResult(RefUpdateOutcome.ExpectedPriorMismatch, current));
        }

        _local[branch] = newTip;
        return Task.FromResult(new RefUpdateResult(RefUpdateOutcome.Updated, newTip));
    }

    public Task<TicketWorktree> PrepareWorktreeAsync(GitRepositoryLocation repo, WorktreeSpec spec, CancellationToken cancellationToken)
    {
        var worktree = new TicketWorktree(spec.Path, spec.Branch, spec.StartPoint);
        _worktrees[spec.Path] = (worktree, WorktreeStatus.Clean);
        _local[spec.Branch] = spec.StartPoint;
        return Task.FromResult(worktree);
    }

    public Task<WorktreeInspection> InspectWorktreeAsync(GitRepositoryLocation repo, string worktreePath, CancellationToken cancellationToken) =>
        Task.FromResult(_worktrees.TryGetValue(worktreePath, out var entry)
            ? new WorktreeInspection(entry.Status, entry.Worktree.Branch, entry.Worktree.Head)
            : new WorktreeInspection(WorktreeStatus.Missing, null, null));

    public Task<GitMergeResult> MergeIntoWorktreeAsync(TicketWorktree worktree, CommitSha source, string message, CancellationToken cancellationToken)
    {
        TicketWorktree current = _worktrees[worktree.Path].Worktree;
        if (Reachable(current.Head).Contains(source))
        {
            return Task.FromResult(GitMergeResult.AlreadyUpToDate(current.Head));
        }

        CommitSha merge = Commit([current.Head, source]);
        _worktrees[worktree.Path] = (current with { Head = merge }, WorktreeStatus.Clean);
        _local[current.Branch] = merge;
        return Task.FromResult(GitMergeResult.Merged(merge));
    }

    public Task<GitMergeResult> CreateSquashCommitAsync(GitRepositoryLocation repo, SquashRequest request, CancellationToken cancellationToken)
    {
        if (ConflictOnNextSquash is { } conflicted)
        {
            ConflictOnNextSquash = null;
            return Task.FromResult(GitMergeResult.Conflicted(conflicted));
        }

        CommitSha squash = Commit([request.IntegrationTip], [.. FilesBetween(request.IntegrationTip, request.Source)]);
        return Task.FromResult(GitMergeResult.Merged(squash));
    }

    public Task<IReadOnlyList<string>> GetChangedFilesAsync(GitRepositoryLocation repo, CommitSha from, CommitSha to, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(FilesBetween(from, to));

    public Task<PushOutcome> PushAsync(GitRepositoryLocation repo, RefPush push, CancellationToken cancellationToken)
    {
        CommitSha? remote = RemoteTip(push.Branch);
        if (remote == push.Commit)
        {
            return Task.FromResult(PushOutcome.AlreadyUpToDate);
        }

        if (remote != push.ExpectedRemoteTip)
        {
            return Task.FromResult(PushOutcome.Rejected);
        }

        _remote[push.Branch] = push.Commit;
        return Task.FromResult(PushOutcome.Pushed);
    }

    public Task<WorktreeCleanupResult> CleanupWorktreeAsync(GitRepositoryLocation repo, string worktreePath, CancellationToken cancellationToken)
    {
        if (!_worktrees.TryGetValue(worktreePath, out var entry))
        {
            return Task.FromResult(new WorktreeCleanupResult(WorktreeCleanupOutcome.AlreadyMissing));
        }

        switch (entry.Status)
        {
            case WorktreeStatus.Dirty:
                return Task.FromResult(new WorktreeCleanupResult(WorktreeCleanupOutcome.RetainedDirty, $"{worktreePath} has uncommitted changes."));
            case WorktreeStatus.Locked:
                return Task.FromResult(new WorktreeCleanupResult(WorktreeCleanupOutcome.RetainedLocked, $"{worktreePath} is locked."));
            default:
                _worktrees.Remove(worktreePath);
                return Task.FromResult(new WorktreeCleanupResult(WorktreeCleanupOutcome.Removed));
        }
    }

    private string[] FilesBetween(CommitSha from, CommitSha to)
    {
        HashSet<CommitSha> excluded = Reachable(from);
        return Reachable(to).Where(sha => !excluded.Contains(sha)).SelectMany(sha => _commits[sha].Files).Distinct().Order().ToArray();
    }

    private HashSet<CommitSha> Reachable(CommitSha tip)
    {
        var seen = new HashSet<CommitSha>();
        var pending = new Stack<CommitSha>([tip]);
        while (pending.TryPop(out CommitSha sha))
        {
            if (seen.Add(sha))
            {
                foreach (CommitSha parent in _commits[sha].Parents)
                {
                    pending.Push(parent);
                }
            }
        }

        return seen;
    }
}
