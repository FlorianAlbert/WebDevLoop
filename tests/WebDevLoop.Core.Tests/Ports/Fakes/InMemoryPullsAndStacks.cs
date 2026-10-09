using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Ports.Fakes;

public sealed class InMemoryPullsAndStacks(Func<BranchName, CommitSha> headOf) : IGitHubPullsAndStacks
{
    private readonly Dictionary<PullRequestNumber, PullRequestSnapshot> _pulls = [];
    private readonly Dictionary<int, PullStackSnapshot> _stacks = [];

    public IReadOnlyCollection<PullRequestSnapshot> PullRequests => _pulls.Values;

    public IReadOnlyCollection<PullStackSnapshot> Stacks => _stacks.Values;

    public StackMergeStatus MergeStatus { get; set; } = StackMergeStatus.Open;

    public Task<PullRequestSnapshot?> FindPullRequestByHeadAsync(GitHubRepoRef repo, BranchName head, CancellationToken cancellationToken) =>
        Task.FromResult(_pulls.Values.FirstOrDefault(pull => pull.Head == head));

    public Task<PullRequestSnapshot> GetPullRequestAsync(GitHubRepoRef repo, PullRequestNumber number, CancellationToken cancellationToken) =>
        Task.FromResult(_pulls[number]);

    public Task<PullRequestSnapshot> CreateDraftPullRequestAsync(GitHubRepoRef repo, DraftPullRequest request, CancellationToken cancellationToken)
    {
        var number = new PullRequestNumber(_pulls.Count + 1);
        var pull = new PullRequestSnapshot(number, request.Head, request.Base, headOf(request.Head), request.Body, IsDraft: true, PullRequestState.Open);
        _pulls[number] = pull;
        return Task.FromResult(pull);
    }

    public Task UpdatePullRequestBaseAsync(GitHubRepoRef repo, PullRequestNumber number, BranchName newBase, CancellationToken cancellationToken)
    {
        _pulls[number] = _pulls[number] with { Base = newBase };
        return Task.CompletedTask;
    }

    public Task MarkReadyForReviewAsync(GitHubRepoRef repo, PullRequestNumber number, CancellationToken cancellationToken)
    {
        _pulls[number] = _pulls[number] with { IsDraft = false };
        return Task.CompletedTask;
    }

    public Task<PullStackSnapshot?> FindStackAsync(GitHubRepoRef repo, PullRequestNumber member, CancellationToken cancellationToken) =>
        Task.FromResult(_stacks.Values.FirstOrDefault(stack => stack.BottomToTop.Contains(member)));

    public Task<PullStackSnapshot> CreateStackAsync(GitHubRepoRef repo, IReadOnlyList<PullRequestNumber> bottomToTop, CancellationToken cancellationToken)
    {
        var stack = new PullStackSnapshot(_stacks.Count + 1, bottomToTop);
        _stacks[stack.StackNumber] = stack;
        return Task.FromResult(stack);
    }

    public Task<PullStackSnapshot> AddToStackAsync(GitHubRepoRef repo, int stackNumber, PullRequestNumber pullRequest, CancellationToken cancellationToken)
    {
        PullStackSnapshot stack = _stacks[stackNumber] with { BottomToTop = [.. _stacks[stackNumber].BottomToTop, pullRequest] };
        _stacks[stackNumber] = stack;
        return Task.FromResult(stack);
    }

    public Task<StackMergeStatus> GetStackMergeStatusAsync(
        GitHubRepoRef repo,
        IReadOnlyList<PullRequestNumber> bottomToTop,
        BranchName trunk,
        CancellationToken cancellationToken) => Task.FromResult(MergeStatus);
}
