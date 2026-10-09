using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Web.Tests.Workflow;

/// <summary>Thread-safe in-memory GitHub issues: spec issues with ticket sub-issues and native "blocked by" relations.</summary>
internal sealed class FakeGitHubIssues : IGitHubIssues
{
    private readonly object _gate = new();
    private readonly Dictionary<int, IssueSnapshot> _issues = [];
    private readonly Dictionary<int, List<int>> _subIssues = [];
    private readonly Dictionary<int, FindingFingerprint> _fingerprints = [];
    private readonly List<(int Issue, string Body)> _comments = [];

    public FakeGitHubIssues(GitHubRepoRef repository) => Repository = repository;

    public GitHubRepoRef Repository { get; }

    public IssueRef Ref(int number) => new(Repository.Owner, Repository.Name, number);

    /// <summary>Seeds an open issue, optionally as sub-issue of <paramref name="parent"/>, blocked by the given issues.</summary>
    public void Seed(int number, string title, int? parent = null, params int[] blockedBy)
    {
        lock (_gate)
        {
            _issues[number] = new IssueSnapshot(Ref(number), title, $"Implement {title}.", IssueState.Open, [.. blockedBy.Select(Ref)]);
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
            return _issues[number].State;
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
        lock (_gate)
        {
            return Task.FromResult(_issues[issue.Number]);
        }
    }

    public Task<SpecIssueGraph> GetSpecGraphAsync(IssueRef specIssue, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IssueSnapshot[] tickets = [.. SubIssuesOf(specIssue.Number).Select(child => _issues[child])];
            DependencyEdge<IssueRef>[] edges = [.. tickets.SelectMany(ticket => ticket.BlockedBy.Select(blocker => new DependencyEdge<IssueRef>(ticket.Ref, blocker)))];
            return Task.FromResult(new SpecIssueGraph(_issues[specIssue.Number], tickets, edges));
        }
    }

    public Task<IssueSnapshot?> FindFindingIssueAsync(IssueRef specIssue, FindingFingerprint fingerprint, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(SubIssuesOf(specIssue.Number)
                .Where(child => _fingerprints.TryGetValue(child, out FindingFingerprint known) && known == fingerprint)
                .Select(child => _issues[child])
                .FirstOrDefault());
        }
    }

    public Task<IssueSnapshot> CreateFindingIssueAsync(FindingIssueDraft draft, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            int number = _issues.Keys.Max() + 1;
            var snapshot = new IssueSnapshot(Ref(number), draft.Title, draft.Body, IssueState.Open, []);
            _issues[number] = snapshot;
            _fingerprints[number] = draft.Fingerprint;
            return Task.FromResult(snapshot);
        }
    }

    public Task AddSubIssueAsync(IssueRef parent, IssueRef child, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            List<int> children = SubIssuesOf(parent.Number);
            if (!children.Contains(child.Number))
            {
                children.Add(child.Number);
            }
        }

        return Task.CompletedTask;
    }

    public Task AddBlockedByAsync(IssueRef blocked, IssueRef blocking, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IssueSnapshot snapshot = _issues[blocked.Number];
            if (!snapshot.BlockedBy.Any(existing => existing.Number == blocking.Number))
            {
                _issues[blocked.Number] = snapshot with { BlockedBy = [.. snapshot.BlockedBy, Ref(blocking.Number)] };
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
            _issues[issue.Number] = _issues[issue.Number] with { State = IssueState.Closed };
        }

        return Task.CompletedTask;
    }

    private List<int> SubIssuesOf(int parent) =>
        _subIssues.TryGetValue(parent, out List<int>? children) ? children : _subIssues[parent] = [];
}
