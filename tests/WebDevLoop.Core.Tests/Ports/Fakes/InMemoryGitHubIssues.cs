using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Ports.Fakes;

public sealed class InMemoryGitHubIssues : IGitHubIssues
{
    private readonly Dictionary<int, IssueSnapshot> _issues = [];
    private readonly Dictionary<int, List<IssueRef>> _subIssues = [];
    private readonly Dictionary<int, FindingFingerprint> _fingerprints = [];

    public List<(IssueRef Issue, string Body)> Comments { get; } = [];

    public IssueSnapshot Seed(IssueRef issue, string title, IssueRef? parent = null, params IssueRef[] blockedBy)
    {
        var snapshot = new IssueSnapshot(issue, title, $"body of {title}", IssueState.Open, blockedBy);
        _issues[issue.Number] = snapshot;
        if (parent is { } parentRef)
        {
            SubIssuesOf(parentRef).Add(issue);
        }

        return snapshot;
    }

    public Task<IssueSnapshot> GetIssueAsync(IssueRef issue, CancellationToken cancellationToken) =>
        Task.FromResult(_issues[issue.Number]);

    public Task<SpecIssueGraph> GetSpecGraphAsync(IssueRef specIssue, CancellationToken cancellationToken)
    {
        IssueSnapshot[] tickets = SubIssuesOf(specIssue).Select(child => _issues[child.Number]).ToArray();
        DependencyEdge<IssueRef>[] edges = tickets
            .SelectMany(ticket => ticket.BlockedBy.Select(blocker => new DependencyEdge<IssueRef>(ticket.Ref, blocker)))
            .ToArray();
        return Task.FromResult(new SpecIssueGraph(_issues[specIssue.Number], tickets, edges));
    }

    public Task<IssueSnapshot?> FindFindingIssueAsync(IssueRef specIssue, FindingFingerprint fingerprint, CancellationToken cancellationToken) =>
        Task.FromResult(SubIssuesOf(specIssue)
            .Where(child => _fingerprints.GetValueOrDefault(child.Number) == fingerprint)
            .Select(child => _issues[child.Number])
            .FirstOrDefault());

    public Task<IssueSnapshot> CreateFindingIssueAsync(FindingIssueDraft draft, CancellationToken cancellationToken)
    {
        var issue = new IssueRef(draft.Parent.Owner, draft.Parent.Repo, _issues.Keys.DefaultIfEmpty().Max() + 1);
        var snapshot = new IssueSnapshot(issue, draft.Title, draft.Body, IssueState.Open, []);
        _issues[issue.Number] = snapshot;
        _fingerprints[issue.Number] = draft.Fingerprint;
        return Task.FromResult(snapshot);
    }

    public Task AddSubIssueAsync(IssueRef parent, IssueRef child, CancellationToken cancellationToken)
    {
        List<IssueRef> children = SubIssuesOf(parent);
        if (!children.Any(existing => existing.Number == child.Number))
        {
            children.Add(child);
        }

        return Task.CompletedTask;
    }

    public Task AddBlockedByAsync(IssueRef blocked, IssueRef blocking, CancellationToken cancellationToken)
    {
        IssueSnapshot snapshot = _issues[blocked.Number];
        if (!snapshot.BlockedBy.Any(existing => existing.Number == blocking.Number))
        {
            _issues[blocked.Number] = snapshot with { BlockedBy = [.. snapshot.BlockedBy, blocking] };
        }

        return Task.CompletedTask;
    }

    public Task CommentAsync(IssueRef issue, string body, CancellationToken cancellationToken)
    {
        Comments.Add((issue, body));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListCommentsAsync(IssueRef issue, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(Comments.Where(comment => comment.Issue.Number == issue.Number).Select(comment => comment.Body).ToArray());

    /// <summary>A finding sub-issue that carries <paramref name="fingerprint"/> in its body, e.g. created right before a crash.</summary>
    public IssueSnapshot SeedFindingIssue(IssueRef issue, string title, IssueRef parent, FindingFingerprint fingerprint)
    {
        IssueSnapshot snapshot = Seed(issue, title, parent);
        _fingerprints[issue.Number] = fingerprint;
        return snapshot;
    }

    /// <summary>A human detaches the sub-issue from its parent on GitHub.</summary>
    public void RemoveSubIssue(IssueRef parent, IssueRef child) => SubIssuesOf(parent).RemoveAll(existing => existing.Number == child.Number);

    /// <summary>A human reopens the issue on GitHub.</summary>
    public void Reopen(IssueRef issue) => _issues[issue.Number] = _issues[issue.Number] with { State = IssueState.Open };

    /// <summary>A human replaces the native "blocked by" relations of the issue on GitHub.</summary>
    public void SetBlockedBy(IssueRef issue, params IssueRef[] blockers) => _issues[issue.Number] = _issues[issue.Number] with { BlockedBy = blockers };

    public Task CloseAsync(IssueRef issue, IssueCloseReason reason, CancellationToken cancellationToken)
    {
        _issues[issue.Number] = _issues[issue.Number] with { State = IssueState.Closed };
        return Task.CompletedTask;
    }

    private List<IssueRef> SubIssuesOf(IssueRef parent) =>
        _subIssues.TryGetValue(parent.Number, out List<IssueRef>? children) ? children : _subIssues[parent.Number] = [];
}
