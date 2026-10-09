using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Ports.Fakes;

namespace WebDevLoop.Core.Tests.Orchestration.Completion.ReadyAndMerge;

/// <summary>
/// PR/stack fake on top of <see cref="InMemoryPullsAndStacks"/> that lets tests play the human side: merge or close PRs,
/// tamper with PRs or the stack outside the app, and fail GitHub calls. Merge status follows the real adapter: a closed
/// layer means closed unmerged, an open layer means open, and an all-merged stack is merged only once trunk contains the top
/// layer's head commit.
/// </summary>
internal sealed class StackPullsFake(InMemoryGitWorkspace git, GitRepositoryLocation location, BranchName trunk) : IGitHubPullsAndStacks
{
    private readonly InMemoryPullsAndStacks _inner = new(branch => git.RemoteTip(branch) ?? throw new InvalidOperationException($"'{branch}' was not pushed."));
    private readonly Dictionary<PullRequestNumber, PullRequestState> _states = [];
    private readonly Dictionary<PullRequestNumber, Func<PullRequestSnapshot, PullRequestSnapshot>> _tampered = [];
    private PullStackSnapshot? _stackOverride;

    /// <summary>Every successful mark-ready call, in call order.</summary>
    public List<PullRequestNumber> MarkedReady { get; } = [];

    /// <summary>PRs whose next mark-ready call fails like an unavailable GitHub API.</summary>
    public HashSet<PullRequestNumber> FailMarkReadyOnce { get; } = [];

    /// <summary>Merge-status polls of stacks matching this predicate (bottom to top) fail.</summary>
    public Func<IReadOnlyList<PullRequestNumber>, bool> FailMergeStatusWhen { get; set; } = _ => false;

    public void Merge(params PullRequestNumber[] numbers)
    {
        foreach (PullRequestNumber number in numbers)
        {
            _states[number] = PullRequestState.Merged;
        }
    }

    public void Close(PullRequestNumber number) => _states[number] = PullRequestState.Closed;

    public void Tamper(PullRequestNumber number, Func<PullRequestSnapshot, PullRequestSnapshot> change) => _tampered[number] = change;

    public void OverrideStack(PullStackSnapshot stack) => _stackOverride = stack;

    /// <summary>GitHub no longer lists any stack, e.g. after the lower part of a stack merged and the rest was unstacked.</summary>
    public bool StacksDissolved { get; set; }

    public PullRequestSnapshot Snapshot(PullRequestNumber number) =>
        Apply(_inner.GetPullRequestAsync(location.Repo, number, CancellationToken.None).GetAwaiter().GetResult());

    public async Task<PullRequestSnapshot?> FindPullRequestByHeadAsync(GitHubRepoRef repo, BranchName head, CancellationToken cancellationToken) =>
        await _inner.FindPullRequestByHeadAsync(repo, head, cancellationToken) is { } pull ? Apply(pull) : null;

    public async Task<PullRequestSnapshot> GetPullRequestAsync(GitHubRepoRef repo, PullRequestNumber number, CancellationToken cancellationToken) =>
        Apply(await _inner.GetPullRequestAsync(repo, number, cancellationToken));

    public Task<PullRequestSnapshot> CreateDraftPullRequestAsync(GitHubRepoRef repo, DraftPullRequest request, CancellationToken cancellationToken) =>
        _inner.CreateDraftPullRequestAsync(repo, request, cancellationToken);

    public Task UpdatePullRequestBaseAsync(GitHubRepoRef repo, PullRequestNumber number, BranchName newBase, CancellationToken cancellationToken) =>
        _inner.UpdatePullRequestBaseAsync(repo, number, newBase, cancellationToken);

    public Task MarkReadyForReviewAsync(GitHubRepoRef repo, PullRequestNumber number, CancellationToken cancellationToken)
    {
        if (FailMarkReadyOnce.Remove(number))
        {
            throw new HttpRequestException("GitHub is unavailable.");
        }

        MarkedReady.Add(number);
        return _inner.MarkReadyForReviewAsync(repo, number, cancellationToken);
    }

    public async Task<PullStackSnapshot?> FindStackAsync(GitHubRepoRef repo, PullRequestNumber member, CancellationToken cancellationToken) =>
        StacksDissolved ? null
        : _stackOverride is { } stack && stack.BottomToTop.Contains(member) ? stack : await _inner.FindStackAsync(repo, member, cancellationToken);

    public Task<PullStackSnapshot> CreateStackAsync(GitHubRepoRef repo, IReadOnlyList<PullRequestNumber> bottomToTop, CancellationToken cancellationToken) =>
        _inner.CreateStackAsync(repo, bottomToTop, cancellationToken);

    public Task<PullStackSnapshot> AddToStackAsync(GitHubRepoRef repo, int stackNumber, PullRequestNumber pullRequest, CancellationToken cancellationToken) =>
        _inner.AddToStackAsync(repo, stackNumber, pullRequest, cancellationToken);

    public async Task<StackMergeStatus> GetStackMergeStatusAsync(
        GitHubRepoRef repo,
        IReadOnlyList<PullRequestNumber> bottomToTop,
        BranchName trunkBranch,
        CancellationToken cancellationToken)
    {
        if (FailMergeStatusWhen(bottomToTop))
        {
            throw new HttpRequestException("GitHub is unavailable.");
        }

        PullRequestSnapshot[] layers = await Task.WhenAll(bottomToTop.Select(number => GetPullRequestAsync(repo, number, cancellationToken)));
        if (layers.Any(layer => layer.State == PullRequestState.Closed))
        {
            return StackMergeStatus.ClosedUnmerged;
        }

        if (layers.Any(layer => layer.State == PullRequestState.Open))
        {
            return StackMergeStatus.Open;
        }

        Assert.Equal(trunk, trunkBranch);
        CommitSha? trunkTip = git.RemoteTip(trunkBranch);
        return trunkTip is { } tip && await git.IsAncestorAsync(location, layers[^1].HeadSha, tip, cancellationToken)
            ? StackMergeStatus.Merged
            : StackMergeStatus.Open;
    }

    private PullRequestSnapshot Apply(PullRequestSnapshot pull)
    {
        PullRequestSnapshot current = _states.TryGetValue(pull.Number, out PullRequestState state) ? pull with { State = state } : pull;
        return _tampered.TryGetValue(pull.Number, out Func<PullRequestSnapshot, PullRequestSnapshot>? change) ? change(current) : current;
    }
}
