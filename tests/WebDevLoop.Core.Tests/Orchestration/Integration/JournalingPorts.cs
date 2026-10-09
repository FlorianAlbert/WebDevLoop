using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Orchestration.Integration;

/// <summary>Passes every call to the in-memory fake; side-effecting calls are journaled (and may crash afterwards).</summary>
internal sealed class JournalingGitWorkspace(IGitWorkspace inner, ExternalCallJournal journal) : IGitWorkspace
{
    public Task EnsureClonedAsync(GitRepositoryLocation repo, CancellationToken cancellationToken) => inner.EnsureClonedAsync(repo, cancellationToken);

    public Task FetchAsync(GitRepositoryLocation repo, CancellationToken cancellationToken)
    {
        journal.ThrowIfDown();
        return inner.FetchAsync(repo, cancellationToken);
    }

    public Task<CommitSha?> GetBranchTipAsync(GitRepositoryLocation repo, BranchName branch, GitRefScope scope, CancellationToken cancellationToken) =>
        inner.GetBranchTipAsync(repo, branch, scope, cancellationToken);

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
        Journal($"update-ref:{branch}", () => inner.UpdateBranchAsync(repo, branch, newTip, expectedPriorTip, cancellationToken));

    public Task<TicketWorktree> PrepareWorktreeAsync(GitRepositoryLocation repo, WorktreeSpec spec, CancellationToken cancellationToken) =>
        Journal($"prepare-worktree:{spec.Branch}", () => inner.PrepareWorktreeAsync(repo, spec, cancellationToken));

    public Task<WorktreeInspection> InspectWorktreeAsync(GitRepositoryLocation repo, string worktreePath, CancellationToken cancellationToken) =>
        inner.InspectWorktreeAsync(repo, worktreePath, cancellationToken);

    public Task<GitMergeResult> MergeIntoWorktreeAsync(TicketWorktree worktree, CommitSha source, string message, CancellationToken cancellationToken) =>
        Journal($"merge-into-worktree:{worktree.Branch}", () => inner.MergeIntoWorktreeAsync(worktree, source, message, cancellationToken));

    public Task<GitMergeResult> CreateSquashCommitAsync(GitRepositoryLocation repo, SquashRequest request, CancellationToken cancellationToken) =>
        Journal("squash", () => inner.CreateSquashCommitAsync(repo, request, cancellationToken));

    public Task<IReadOnlyList<string>> GetChangedFilesAsync(GitRepositoryLocation repo, CommitSha from, CommitSha to, CancellationToken cancellationToken) =>
        inner.GetChangedFilesAsync(repo, from, to, cancellationToken);

    public Task<PushOutcome> PushAsync(GitRepositoryLocation repo, RefPush push, CancellationToken cancellationToken) =>
        Journal($"push:{push.Branch}", () => inner.PushAsync(repo, push, cancellationToken));

    public Task<WorktreeCleanupResult> CleanupWorktreeAsync(GitRepositoryLocation repo, string worktreePath, CancellationToken cancellationToken) =>
        Journal($"cleanup-worktree:{worktreePath}", () => inner.CleanupWorktreeAsync(repo, worktreePath, cancellationToken));

    private async Task<T> Journal<T>(string call, Func<Task<T>> action)
    {
        await journal.BeforeAsync(call);
        T result = await action();
        journal.Record(call);
        return result;
    }
}

internal sealed class JournalingPullsAndStacks(IGitHubPullsAndStacks inner, ExternalCallJournal journal) : IGitHubPullsAndStacks
{
    public List<DraftPullRequest> Drafts { get; } = [];

    /// <summary>Thrown once by the next pull request creation, before anything is created (a transient GitHub failure).</summary>
    public Exception? FailNextCreate { get; set; }

    public Task<PullRequestSnapshot?> FindPullRequestByHeadAsync(GitHubRepoRef repo, BranchName head, CancellationToken cancellationToken) =>
        inner.FindPullRequestByHeadAsync(repo, head, cancellationToken);

    public Task<PullRequestSnapshot> GetPullRequestAsync(GitHubRepoRef repo, PullRequestNumber number, CancellationToken cancellationToken) =>
        inner.GetPullRequestAsync(repo, number, cancellationToken);

    public async Task<PullRequestSnapshot> CreateDraftPullRequestAsync(GitHubRepoRef repo, DraftPullRequest request, CancellationToken cancellationToken)
    {
        if (FailNextCreate is { } failure)
        {
            FailNextCreate = null;
            throw failure;
        }

        Drafts.Add(request);
        return await Journal($"create-pr:{request.Head}", () => inner.CreateDraftPullRequestAsync(repo, request, cancellationToken));
    }

    public Task UpdatePullRequestBaseAsync(GitHubRepoRef repo, PullRequestNumber number, BranchName newBase, CancellationToken cancellationToken) =>
        Journal($"update-base:{number}", async () =>
        {
            await inner.UpdatePullRequestBaseAsync(repo, number, newBase, cancellationToken);
            return true;
        });

    public Task MarkReadyForReviewAsync(GitHubRepoRef repo, PullRequestNumber number, CancellationToken cancellationToken) =>
        Journal($"mark-ready:{number}", async () =>
        {
            await inner.MarkReadyForReviewAsync(repo, number, cancellationToken);
            return true;
        });

    public Task<PullStackSnapshot?> FindStackAsync(GitHubRepoRef repo, PullRequestNumber member, CancellationToken cancellationToken) =>
        inner.FindStackAsync(repo, member, cancellationToken);

    public Task<PullStackSnapshot> CreateStackAsync(GitHubRepoRef repo, IReadOnlyList<PullRequestNumber> bottomToTop, CancellationToken cancellationToken) =>
        Journal($"create-stack:{string.Join(',', bottomToTop)}", () => inner.CreateStackAsync(repo, bottomToTop, cancellationToken));

    public Task<PullStackSnapshot> AddToStackAsync(GitHubRepoRef repo, int stackNumber, PullRequestNumber pullRequest, CancellationToken cancellationToken) =>
        Journal($"add-to-stack:{stackNumber}:{pullRequest}", () => inner.AddToStackAsync(repo, stackNumber, pullRequest, cancellationToken));

    public Task<StackMergeStatus> GetStackMergeStatusAsync(
        GitHubRepoRef repo,
        IReadOnlyList<PullRequestNumber> bottomToTop,
        BranchName trunk,
        CancellationToken cancellationToken) => inner.GetStackMergeStatusAsync(repo, bottomToTop, trunk, cancellationToken);

    private async Task<T> Journal<T>(string call, Func<Task<T>> action)
    {
        await journal.BeforeAsync(call);
        T result = await action();
        journal.Record(call);
        return result;
    }
}

internal sealed class JournalingGitHubIssues(IGitHubIssues inner, ExternalCallJournal journal) : IGitHubIssues
{
    public Task<IssueSnapshot> GetIssueAsync(IssueRef issue, CancellationToken cancellationToken) => inner.GetIssueAsync(issue, cancellationToken);

    public Task<SpecIssueGraph> GetSpecGraphAsync(IssueRef specIssue, CancellationToken cancellationToken) => inner.GetSpecGraphAsync(specIssue, cancellationToken);

    public Task<IssueSnapshot?> FindFindingIssueAsync(IssueRef specIssue, FindingFingerprint fingerprint, CancellationToken cancellationToken) =>
        inner.FindFindingIssueAsync(specIssue, fingerprint, cancellationToken);

    public Task<IssueSnapshot> CreateFindingIssueAsync(FindingIssueDraft draft, CancellationToken cancellationToken) =>
        Journal($"create-issue:{draft.Title}", () => inner.CreateFindingIssueAsync(draft, cancellationToken));

    public Task AddSubIssueAsync(IssueRef parent, IssueRef child, CancellationToken cancellationToken) =>
        Journal($"add-sub-issue:{child}", () => inner.AddSubIssueAsync(parent, child, cancellationToken));

    public Task AddBlockedByAsync(IssueRef blocked, IssueRef blocking, CancellationToken cancellationToken) =>
        Journal($"add-blocked-by:{blocked}", () => inner.AddBlockedByAsync(blocked, blocking, cancellationToken));

    public Task CommentAsync(IssueRef issue, string body, CancellationToken cancellationToken) =>
        Journal($"comment:{issue}", () => inner.CommentAsync(issue, body, cancellationToken));

    public Task CloseAsync(IssueRef issue, IssueCloseReason reason, CancellationToken cancellationToken) =>
        Journal($"close:{issue}", () => inner.CloseAsync(issue, reason, cancellationToken));

    private async Task<T> Journal<T>(string call, Func<Task<T>> action)
    {
        await journal.BeforeAsync(call);
        T result = await action();
        journal.Record(call);
        return result;
    }

    private Task Journal(string call, Func<Task> action) =>
        Journal(call, async () =>
        {
            await action();
            return true;
        });
}
