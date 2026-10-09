using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Stacks;

namespace WebDevLoop.Infrastructure.GitHub.Pulls;

/// <summary>
/// <see cref="IGitHubPullsAndStacks"/> over PR REST, stack REST and GraphQL (mark ready). <paramref name="ghFallback"/> is
/// optional and only used for stack create/add when the stack REST endpoints answer 404.
/// </summary>
public sealed class GitHubPullsAndStacks : IGitHubPullsAndStacks
{
    private readonly PullRequestsClient _pulls;
    private readonly StacksClient _stacks;
    private readonly StackMergeTracker _mergeTracker;

    public GitHubPullsAndStacks(HttpClient http, ITokenProvider tokens, GitHubPullsOptions options, IGhCommandRunner? ghFallback = null)
    {
        var api = new GitHubApiClient(http, tokens, options);
        _pulls = new PullRequestsClient(api);
        _stacks = new StacksClient(api, ghFallback);
        _mergeTracker = new StackMergeTracker(_pulls);
    }

    public async Task<PullRequestSnapshot?> FindPullRequestByHeadAsync(GitHubRepoRef repo, BranchName head, CancellationToken cancellationToken) =>
        (await _pulls.FindByHeadAsync(repo, head, cancellationToken))?.ToSnapshot();

    public async Task<PullRequestSnapshot> GetPullRequestAsync(GitHubRepoRef repo, PullRequestNumber number, CancellationToken cancellationToken) =>
        (await _pulls.GetAsync(repo, number, cancellationToken)).ToSnapshot();

    public async Task<PullRequestSnapshot> CreateDraftPullRequestAsync(GitHubRepoRef repo, DraftPullRequest request, CancellationToken cancellationToken) =>
        (await _pulls.CreateDraftAsync(repo, request, cancellationToken)).ToSnapshot();

    public Task UpdatePullRequestBaseAsync(GitHubRepoRef repo, PullRequestNumber number, BranchName newBase, CancellationToken cancellationToken) =>
        _pulls.UpdateBaseAsync(repo, number, newBase, cancellationToken);

    public Task MarkReadyForReviewAsync(GitHubRepoRef repo, PullRequestNumber number, CancellationToken cancellationToken) =>
        _pulls.MarkReadyAsync(repo, number, cancellationToken);

    public Task<PullStackSnapshot?> FindStackAsync(GitHubRepoRef repo, PullRequestNumber member, CancellationToken cancellationToken) =>
        _stacks.FindAsync(repo, member, cancellationToken);

    public Task<PullStackSnapshot> CreateStackAsync(GitHubRepoRef repo, IReadOnlyList<PullRequestNumber> bottomToTop, CancellationToken cancellationToken) =>
        _stacks.CreateAsync(repo, bottomToTop, cancellationToken);

    public Task<PullStackSnapshot> AddToStackAsync(GitHubRepoRef repo, int stackNumber, PullRequestNumber pullRequest, CancellationToken cancellationToken) =>
        _stacks.AddAsync(repo, stackNumber, pullRequest, cancellationToken);

    public Task<StackMergeStatus> GetStackMergeStatusAsync(
        GitHubRepoRef repo,
        IReadOnlyList<PullRequestNumber> bottomToTop,
        BranchName trunk,
        CancellationToken cancellationToken) => _mergeTracker.GetStatusAsync(repo, bottomToTop, trunk, cancellationToken);
}
