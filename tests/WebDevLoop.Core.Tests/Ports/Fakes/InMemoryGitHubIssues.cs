using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Ports.Fakes;

public sealed class InMemoryGitHubIssues : IGitHubIssues
{
    private readonly Dictionary<(string Owner, string Repo, int Number), IssueSnapshot> _issues = [];
    private readonly Dictionary<(string Owner, string Repo, int Number), List<IssueRef>> _subIssues = [];
    private readonly Dictionary<(string Owner, string Repo, int Number), FindingFingerprint> _fingerprints = [];

    public List<(IssueRef Issue, string Body)> Comments { get; } = [];

    public void Seed(IssueSnapshot snapshot) => _issues[Key(snapshot.Ref)] = snapshot;

    public IssueSnapshot Seed(IssueRef issue, string title, IssueRef? parent = null, params IssueRef[] blockedBy)
    {
        var snapshot = new IssueSnapshot(issue, title, $"body of {title}", IssueState.Open, blockedBy)
        {
            Labels = [WebDevLoopOptIn.Label],
        };
        _issues[Key(issue)] = snapshot;
        if (parent is { } parentRef)
        {
            SubIssuesOf(parentRef).Add(issue);
        }

        return snapshot;
    }

    public Task<IssueSnapshot> GetIssueAsync(IssueRef issue, CancellationToken cancellationToken) =>
        _issues.TryGetValue(Key(issue), out IssueSnapshot? snapshot)
            ? Task.FromResult(snapshot)
            : throw new GitHubIssuesException($"GitHub issue {issue} is unavailable.");

    public Task<SpecIssueGraph> GetSpecGraphAsync(IssueRef specIssue, CancellationToken cancellationToken)
    {
        IssueSnapshot[] tickets = SubIssuesOf(specIssue).Select(child => _issues[Key(child)]).ToArray();
        DependencyEdge<IssueRef>[] edges = tickets
            .SelectMany(ticket => ticket.BlockedBy.Select(blocker => new DependencyEdge<IssueRef>(ticket.Ref, blocker)))
            .ToArray();
        return Task.FromResult(new SpecIssueGraph(_issues[Key(specIssue)], tickets, edges));
    }

    public async Task<IReadOnlyList<IssueSnapshot>> GetSubIssuesAsync(IssueRef parent, CancellationToken cancellationToken) =>
        (await GetSpecGraphAsync(parent, cancellationToken)).Tickets;

    public Task<IssueSnapshot?> FindFindingIssueAsync(IssueRef specIssue, FindingFingerprint fingerprint, CancellationToken cancellationToken) =>
        Task.FromResult(SubIssuesOf(specIssue)
            .Where(child => _fingerprints.GetValueOrDefault(Key(child)) == fingerprint)
            .Select(child => _issues[Key(child)])
            .FirstOrDefault());

    public Task<IssueSnapshot> CreateFindingIssueAsync(FindingIssueDraft draft, CancellationToken cancellationToken)
    {
        var issue = new IssueRef(draft.Parent.Owner, draft.Parent.Repo, _issues.Keys.Select(key => key.Number).DefaultIfEmpty().Max() + 1);
        var snapshot = new IssueSnapshot(issue, draft.Title, draft.Body, IssueState.Open, [])
        {
            Labels = [WebDevLoopOptIn.Label],
        };
        _issues[Key(issue)] = snapshot;
        _fingerprints[Key(issue)] = draft.Fingerprint;
        return Task.FromResult(snapshot);
    }

    public Task AddSubIssueAsync(IssueRef parent, IssueRef child, CancellationToken cancellationToken)
    {
        List<IssueRef> children = SubIssuesOf(parent);
        if (!children.Any(existing => Key(existing) == Key(child)))
        {
            children.Add(child);
        }

        return Task.CompletedTask;
    }

    public Task AddBlockedByAsync(IssueRef blocked, IssueRef blocking, CancellationToken cancellationToken)
    {
        IssueSnapshot snapshot = _issues[Key(blocked)];
        if (!snapshot.BlockedBy.Any(existing => Key(existing) == Key(blocking)))
        {
            _issues[Key(blocked)] = snapshot with { BlockedBy = [.. snapshot.BlockedBy, blocking] };
        }

        return Task.CompletedTask;
    }

    public Task CommentAsync(IssueRef issue, string body, CancellationToken cancellationToken)
    {
        Comments.Add((issue, body));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListCommentsAsync(IssueRef issue, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(Comments.Where(comment => Key(comment.Issue) == Key(issue)).Select(comment => comment.Body).ToArray());

    /// <summary>A finding sub-issue that carries <paramref name="fingerprint"/> in its body, e.g. created right before a crash.</summary>
    public IssueSnapshot SeedFindingIssue(IssueRef issue, string title, IssueRef parent, FindingFingerprint fingerprint)
    {
        IssueSnapshot snapshot = Seed(issue, title, parent);
        _fingerprints[Key(issue)] = fingerprint;
        return snapshot;
    }

    /// <summary>A human detaches the sub-issue from its parent on GitHub.</summary>
    public void RemoveSubIssue(IssueRef parent, IssueRef child) => SubIssuesOf(parent).RemoveAll(existing => Key(existing) == Key(child));

    /// <summary>A human reopens the issue on GitHub.</summary>
    public void Reopen(IssueRef issue) => _issues[Key(issue)] = _issues[Key(issue)] with { State = IssueState.Open };

    /// <summary>A human replaces the native "blocked by" relations of the issue on GitHub.</summary>
    public void SetBlockedBy(IssueRef issue, params IssueRef[] blockers) => _issues[Key(issue)] = _issues[Key(issue)] with { BlockedBy = blockers };

    public void RemoveIssue(IssueRef issue) => _issues.Remove(Key(issue));

    public Task CloseAsync(IssueRef issue, IssueCloseReason reason, CancellationToken cancellationToken)
    {
        _issues[Key(issue)] = _issues[Key(issue)] with { State = IssueState.Closed };
        return Task.CompletedTask;
    }

    private List<IssueRef> SubIssuesOf(IssueRef parent) =>
        _subIssues.TryGetValue(Key(parent), out List<IssueRef>? children) ? children : _subIssues[Key(parent)] = [];

    private static (string Owner, string Repo, int Number) Key(IssueRef issue) =>
        (issue.Owner.ToUpperInvariant(), issue.Repo.ToUpperInvariant(), issue.Number);
}
