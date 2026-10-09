using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Web.Tests.Workflow;

/// <summary>
/// Thread-safe in-memory GitHub pull requests and stacks. A PR's head commit is the current tip of its head branch on the
/// sandbox remote, as on GitHub. <see cref="FailNextCreation"/> simulates a lost response: the PR is created, but the
/// call fails.
/// </summary>
internal sealed class FakeGitHubPulls(WorkflowSandbox sandbox) : IGitHubPullsAndStacks
{
    private const int FirstNumber = 1000;
    private readonly object _gate = new();
    private readonly List<PullRequest> _pulls = [];
    private readonly List<List<PullRequestNumber>> _stacks = [];
    private int _failNextCreations;

    public int CreateCalls { get; private set; }

    public void FailNextCreation() => Interlocked.Increment(ref _failNextCreations);

    public IReadOnlyList<PullRequestSnapshot> All()
    {
        lock (_gate)
        {
            return [.. _pulls.Select(Snapshot)];
        }
    }

    public IReadOnlyList<IReadOnlyList<PullRequestNumber>> Stacks()
    {
        lock (_gate)
        {
            return [.. _stacks.Select(stack => (IReadOnlyList<PullRequestNumber>)[.. stack])];
        }
    }

    /// <summary>A human merges the PRs bottom-up: GitHub retargets PRs based on a merged head branch to trunk.</summary>
    public void Merge(IReadOnlyList<PullRequestNumber> bottomToTop)
    {
        lock (_gate)
        {
            foreach (PullRequestNumber number in bottomToTop)
            {
                PullRequest merged = Find(number);
                merged.State = PullRequestState.Merged;
                foreach (PullRequest dependent in _pulls.Where(pull => pull.Base == merged.Head && pull.State == PullRequestState.Open))
                {
                    dependent.Base = WorkflowSandbox.Trunk;
                }
            }
        }
    }

    public Task<PullRequestSnapshot?> FindPullRequestByHeadAsync(GitHubRepoRef repo, BranchName head, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_pulls.LastOrDefault(pull => pull.Head == head) is { } pull ? Snapshot(pull) : null);
        }
    }

    public Task<PullRequestSnapshot> GetPullRequestAsync(GitHubRepoRef repo, PullRequestNumber number, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(Snapshot(Find(number)));
        }
    }

    public Task<PullRequestSnapshot> CreateDraftPullRequestAsync(GitHubRepoRef repo, DraftPullRequest request, CancellationToken cancellationToken)
    {
        PullRequestSnapshot created;
        lock (_gate)
        {
            CreateCalls++;
            var pull = new PullRequest(new PullRequestNumber(FirstNumber + _pulls.Count), request.Head, request.Body) { Base = request.Base };
            _pulls.Add(pull);
            created = Snapshot(pull);
        }

        if (Interlocked.Decrement(ref _failNextCreations) >= 0)
        {
            throw new HttpRequestException("Simulated lost response from GitHub after the pull request was created.");
        }

        Interlocked.Exchange(ref _failNextCreations, 0);
        return Task.FromResult(created);
    }

    public Task UpdatePullRequestBaseAsync(GitHubRepoRef repo, PullRequestNumber number, BranchName newBase, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Find(number).Base = newBase;
        }

        return Task.CompletedTask;
    }

    public Task MarkReadyForReviewAsync(GitHubRepoRef repo, PullRequestNumber number, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Find(number).IsDraft = false;
        }

        return Task.CompletedTask;
    }

    public Task<PullStackSnapshot?> FindStackAsync(GitHubRepoRef repo, PullRequestNumber member, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            int index = _stacks.FindIndex(stack => stack.Contains(member));
            return Task.FromResult(index < 0 ? null : StackSnapshot(index));
        }
    }

    public Task<PullStackSnapshot> CreateStackAsync(GitHubRepoRef repo, IReadOnlyList<PullRequestNumber> bottomToTop, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _stacks.Add([.. bottomToTop]);
            return Task.FromResult(StackSnapshot(_stacks.Count - 1))!;
        }
    }

    public Task<PullStackSnapshot> AddToStackAsync(GitHubRepoRef repo, int stackNumber, PullRequestNumber pullRequest, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            List<PullRequestNumber> stack = _stacks[stackNumber - 1];
            if (!stack.Contains(pullRequest))
            {
                stack.Add(pullRequest);
            }

            return Task.FromResult(StackSnapshot(stackNumber - 1))!;
        }
    }

    public Task<StackMergeStatus> GetStackMergeStatusAsync(
        GitHubRepoRef repo,
        IReadOnlyList<PullRequestNumber> bottomToTop,
        BranchName trunk,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            PullRequest[] pulls = [.. bottomToTop.Select(Find)];
            StackMergeStatus status = pulls.All(pull => pull.State == PullRequestState.Merged) ? StackMergeStatus.Merged
                : pulls.Any(pull => pull.State == PullRequestState.Closed) ? StackMergeStatus.ClosedUnmerged
                : StackMergeStatus.Open;
            return Task.FromResult(status);
        }
    }

    private PullRequest Find(PullRequestNumber number) => _pulls.Single(pull => pull.Number == number);

    private PullStackSnapshot StackSnapshot(int index) => new(index + 1, [.. _stacks[index]]);

    private PullRequestSnapshot Snapshot(PullRequest pull) =>
        new(pull.Number, pull.Head, pull.Base, sandbox.RemoteTip(pull.Head) ?? throw new InvalidOperationException($"Head branch '{pull.Head}' was never pushed."), pull.Body, pull.IsDraft, pull.State);

    private sealed class PullRequest(PullRequestNumber number, BranchName head, string body)
    {
        public PullRequestNumber Number { get; } = number;

        public BranchName Head { get; } = head;

        public string Body { get; } = body;

        public BranchName Base { get; set; }

        public bool IsDraft { get; set; } = true;

        public PullRequestState State { get; set; } = PullRequestState.Open;
    }
}
