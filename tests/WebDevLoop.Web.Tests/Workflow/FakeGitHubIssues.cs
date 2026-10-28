using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Web.Tests.Workflow;

/// <summary>Thread-safe in-memory GitHub issues: spec issues with ticket sub-issues and native "blocked by" relations.</summary>
internal sealed class FakeGitHubIssues : IGitHubIssues
{
    private readonly object _gate = new();
    private readonly Dictionary<(string Owner, string Repo, int Number), IssueSnapshot> _issues = [];
    private readonly Dictionary<(string Owner, string Repo, int Number), Exception> _readFailures = [];
    private readonly Dictionary<int, List<int>> _subIssues = [];
    private readonly Dictionary<int, FindingFingerprint> _fingerprints = [];
    private readonly List<(int Issue, string Body)> _comments = [];

    public FakeGitHubIssues(GitHubRepoRef repository) => Repository = repository;

    public GitHubRepoRef Repository { get; }

    public Exception? IssueReadFailure { get; set; }

    public Exception? SubIssueReadFailure { get; set; }

    public Exception? FindingCreationFailure { get; set; }

    public Exception? FindingLinkFailure { get; set; }

    public Exception? FindingResponseFailure { get; set; }

    public TaskCompletionSource FindingIssuanceFailed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IssueRef Ref(int number) => new(Repository.Owner, Repository.Name, number);

    /// <summary>Seeds an open issue, optionally as sub-issue of <paramref name="parent"/>, blocked by the given issues.</summary>
    public void Seed(int number, string title, int? parent = null, params int[] blockedBy)
    {
        lock (_gate)
        {
            _issues[Key(Ref(number))] = new IssueSnapshot(Ref(number), title, $"Implement {title}.", IssueState.Open, [.. blockedBy.Select(Ref)])
            {
                Labels = [WebDevLoopOptIn.Label],
            };
            if (parent is { } parentNumber)
            {
                SubIssuesOf(parentNumber).Add(number);
            }
        }
    }

    public IssueState StateOf(int number)
    {
        lock (_gate)
        {
            return _issues[Key(Ref(number))].State;
        }
    }

    public void SetLabels(int number, params string[] labels)
    {
        lock (_gate)
        {
            _issues[Key(Ref(number))] = _issues[Key(Ref(number))] with { Labels = labels };
        }
    }

    public void Seed(IssueRef issue, IssueState state)
    {
        lock (_gate)
        {
            _issues[Key(issue)] = new IssueSnapshot(issue, "External blocker", "", state, []);
        }
    }

    public void SetBlockedBy(int number, params IssueRef[] blockers)
    {
        lock (_gate)
        {
            var key = Key(Ref(number));
            _issues[key] = _issues[key] with { BlockedBy = blockers };
        }
    }

    public void FailReads(IssueRef issue, Exception? failure)
    {
        lock (_gate)
        {
            if (failure is null)
            {
                _readFailures.Remove(Key(issue));
            }
            else
            {
                _readFailures[Key(issue)] = failure;
            }
        }
    }

    public void SetBody(int number, string body)
    {
        lock (_gate)
        {
            var key = Key(Ref(number));
            _issues[key] = _issues[key] with { Body = body };
        }
    }

    public IReadOnlyList<string> CommentsOn(int number)
    {
        lock (_gate)
        {
            return [.. _comments.Where(comment => comment.Issue == number).Select(comment => comment.Body)];
        }
    }

    public Task<IssueSnapshot> GetIssueAsync(IssueRef issue, CancellationToken cancellationToken)
    {
        if (IssueReadFailure is { } failure)
        {
            throw failure;
        }

        lock (_gate)
        {
            if (_readFailures.TryGetValue(Key(issue), out Exception? error))
            {
                throw error;
            }

            return Task.FromResult(_issues[Key(issue)]);
        }
    }

    public Task<SpecIssueGraph> GetSpecGraphAsync(IssueRef specIssue, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IssueSnapshot[] tickets = [.. SubIssuesOf(specIssue.Number).Select(child => _issues[Key(Ref(child))])];
            DependencyEdge<IssueRef>[] edges = [.. tickets.SelectMany(ticket => ticket.BlockedBy.Select(blocker => new DependencyEdge<IssueRef>(ticket.Ref, blocker)))];
            return Task.FromResult(new SpecIssueGraph(_issues[Key(specIssue)], tickets, edges));
        }
    }

    public async Task<IReadOnlyList<IssueSnapshot>> GetSubIssuesAsync(IssueRef parent, CancellationToken cancellationToken)
    {
        if (SubIssueReadFailure is { } failure)
        {
            throw failure;
        }

        return (await GetSpecGraphAsync(parent, cancellationToken)).Tickets;
    }

    public Task<IssueSnapshot?> FindFindingIssueAsync(IssueRef specIssue, FindingFingerprint fingerprint, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(SubIssuesOf(specIssue.Number)
                .Where(child => _fingerprints.TryGetValue(child, out FindingFingerprint known) && known == fingerprint)
                .Select(child => _issues[Key(Ref(child))])
                .FirstOrDefault());
        }
    }

    public Task<IssueSnapshot> CreateFindingIssueAsync(FindingIssueDraft draft, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            int? existing = _fingerprints.Where(pair => pair.Value == draft.Fingerprint).Select(pair => (int?)pair.Key).FirstOrDefault();
            if (existing is { } known)
            {
                LinkFinding(draft.Parent.Number, known);
                return Task.FromResult(_issues[Key(Ref(known))]);
            }

            ThrowFindingFailure(FindingCreationFailure);
            int number = _issues.Values.Where(issue => issue.Ref.Owner == Repository.Owner && issue.Ref.Repo == Repository.Name).Max(issue => issue.Ref.Number) + 1;
            var snapshot = new IssueSnapshot(
                new IssueRef(Repository.Owner, Repository.Name, number, databaseId: 9000L + number),
                draft.Title, draft.Body, IssueState.Open, [])
            {
                Labels = [WebDevLoopOptIn.Label],
            };
            _issues[Key(Ref(number))] = snapshot;
            _fingerprints[number] = draft.Fingerprint;
            LinkFinding(draft.Parent.Number, number);
            ThrowFindingFailure(FindingResponseFailure);
            return Task.FromResult(snapshot);
        }
    }

    public Task AddSubIssueAsync(IssueRef parent, IssueRef child, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            LinkFinding(parent.Number, child.Number);
        }

        return Task.CompletedTask;
    }

    public Task AddBlockedByAsync(IssueRef blocked, IssueRef blocking, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IssueSnapshot snapshot = _issues[Key(blocked)];
            if (!snapshot.BlockedBy.Any(existing => Key(existing) == Key(blocking)))
            {
                _issues[Key(blocked)] = snapshot with { BlockedBy = [.. snapshot.BlockedBy, blocking] };
            }
        }

        return Task.CompletedTask;
    }

    public Task CommentAsync(IssueRef issue, string body, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _comments.Add((issue.Number, body));
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListCommentsAsync(IssueRef issue, CancellationToken cancellationToken) =>
        Task.FromResult(CommentsOn(issue.Number));

    public Task CloseAsync(IssueRef issue, IssueCloseReason reason, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _issues[Key(issue)] = _issues[Key(issue)] with { State = IssueState.Closed };
        }

        return Task.CompletedTask;
    }

    private List<int> SubIssuesOf(int parent) =>
        _subIssues.TryGetValue(parent, out List<int>? children) ? children : _subIssues[parent] = [];

    private static (string Owner, string Repo, int Number) Key(IssueRef issue) =>
        (issue.Owner.ToUpperInvariant(), issue.Repo.ToUpperInvariant(), issue.Number);

    private void LinkFinding(int parent, int child)
    {
        List<int> children = SubIssuesOf(parent);
        if (!children.Contains(child))
        {
            ThrowFindingFailure(FindingLinkFailure);
            children.Add(child);
        }
    }

    private void ThrowFindingFailure(Exception? failure)
    {
        if (failure is not null)
        {
            FindingIssuanceFailed.TrySetResult();
            throw failure;
        }
    }
}
