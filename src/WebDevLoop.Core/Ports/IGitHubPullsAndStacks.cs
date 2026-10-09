using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

/// <summary>Pull request and stacked-PR operations performed by the app (never by agents).</summary>
public interface IGitHubPullsAndStacks
{
    /// <summary>Reconciliation lookup by exact head ref before creating a PR.</summary>
    Task<PullRequestSnapshot?> FindPullRequestByHeadAsync(GitHubRepoRef repo, BranchName head, CancellationToken cancellationToken);

    Task<PullRequestSnapshot> GetPullRequestAsync(GitHubRepoRef repo, PullRequestNumber number, CancellationToken cancellationToken);

    Task<PullRequestSnapshot> CreateDraftPullRequestAsync(GitHubRepoRef repo, DraftPullRequest request, CancellationToken cancellationToken);

    Task UpdatePullRequestBaseAsync(GitHubRepoRef repo, PullRequestNumber number, BranchName newBase, CancellationToken cancellationToken);

    /// <summary>Idempotent: marking an already-ready PR succeeds.</summary>
    Task MarkReadyForReviewAsync(GitHubRepoRef repo, PullRequestNumber number, CancellationToken cancellationToken);

    Task<PullStackSnapshot?> FindStackAsync(GitHubRepoRef repo, PullRequestNumber member, CancellationToken cancellationToken);

    Task<PullStackSnapshot> CreateStackAsync(GitHubRepoRef repo, IReadOnlyList<PullRequestNumber> bottomToTop, CancellationToken cancellationToken);

    /// <summary>Adds <paramref name="pullRequest"/> as the new top layer.</summary>
    Task<PullStackSnapshot> AddToStackAsync(GitHubRepoRef repo, int stackNumber, PullRequestNumber pullRequest, CancellationToken cancellationToken);

    Task<StackMergeStatus> GetStackMergeStatusAsync(
        GitHubRepoRef repo,
        IReadOnlyList<PullRequestNumber> bottomToTop,
        BranchName trunk,
        CancellationToken cancellationToken);
}
