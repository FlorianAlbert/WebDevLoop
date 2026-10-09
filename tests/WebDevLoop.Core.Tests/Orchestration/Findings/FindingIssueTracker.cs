using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Orchestration.Findings;

/// <summary>Thrown by fakes to model the app process dying at a precise point between external side effects and saves.</summary>
internal sealed class SimulatedCrashException(string message) : Exception(message);

/// <summary>
/// In-memory GitHub issue tracker for finding tickets: issues get database ids, finding issues are linked as sub-issues of
/// their parent (like the WP-08 adapter), blocked-by relations are recorded, and creation can fail just before or right
/// after the issue exists to model crashes around the external side effect.
/// </summary>
internal sealed class FindingIssueTracker : IGitHubIssues
{
    private const long DatabaseIdOffset = 9000;

    private readonly Dictionary<int, IssueSnapshot> _issues = [];
    private readonly Dictionary<int, FindingFingerprint> _fingerprints = [];
    private readonly Dictionary<int, List<int>> _subIssues = [];
    private int _nextNumber = 100;

    public List<FindingIssueDraft> CreatedDrafts { get; } = [];

    public List<(IssueRef Blocked, IssueRef Blocking)> BlockedByCalls { get; } = [];

    public int FindCalls { get; private set; }

    public bool FailBeforeNextCreate { get; set; }

    public bool CrashAfterNextCreate { get; set; }

    /// <summary>Runs inside <see cref="CreateFindingIssueAsync"/> before the issue exists (e.g. to inspect persisted state).</summary>
    public Action<FindingIssueDraft>? BeforeCreate { get; set; }

    public IssueSnapshot Issue(int number) => _issues[number];

    public IReadOnlyList<int> SubIssueNumbers(IssueRef parent) => _subIssues.GetValueOrDefault(parent.Number, []);

    public Task<IssueSnapshot> GetIssueAsync(IssueRef issue, CancellationToken cancellationToken) => Task.FromResult(_issues[issue.Number]);

    public Task<SpecIssueGraph> GetSpecGraphAsync(IssueRef specIssue, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IssueSnapshot?> FindFindingIssueAsync(IssueRef specIssue, FindingFingerprint fingerprint, CancellationToken cancellationToken)
    {
        FindCalls++;
        return Task.FromResult(SubIssueNumbers(specIssue)
            .Where(number => _fingerprints.GetValueOrDefault(number) == fingerprint)
            .Select(number => _issues[number])
            .FirstOrDefault());
    }

    public Task<IssueSnapshot> CreateFindingIssueAsync(FindingIssueDraft draft, CancellationToken cancellationToken)
    {
        BeforeCreate?.Invoke(draft);
        if (FailBeforeNextCreate)
        {
            FailBeforeNextCreate = false;
            throw new SimulatedCrashException("Crashed before the finding issue was created.");
        }

        int number = _nextNumber++;
        var issue = new IssueRef(draft.Parent.Owner, draft.Parent.Repo, number, $"node-{number}", DatabaseIdOffset + number);
        var snapshot = new IssueSnapshot(issue, draft.Title, draft.Body, IssueState.Open, []);
        _issues[number] = snapshot;
        _fingerprints[number] = draft.Fingerprint;
        Link(draft.Parent, number);
        CreatedDrafts.Add(draft);
        if (CrashAfterNextCreate)
        {
            CrashAfterNextCreate = false;
            throw new SimulatedCrashException("Crashed after the finding issue was created.");
        }

        return Task.FromResult(snapshot);
    }

    public Task AddSubIssueAsync(IssueRef parent, IssueRef child, CancellationToken cancellationToken)
    {
        Link(parent, child.Number);
        return Task.CompletedTask;
    }

    public Task AddBlockedByAsync(IssueRef blocked, IssueRef blocking, CancellationToken cancellationToken)
    {
        BlockedByCalls.Add((blocked, blocking));
        IssueSnapshot snapshot = _issues[blocked.Number];
        if (!snapshot.BlockedBy.Any(existing => existing.Number == blocking.Number))
        {
            _issues[blocked.Number] = snapshot with { BlockedBy = [.. snapshot.BlockedBy, blocking] };
        }

        return Task.CompletedTask;
    }

    public Task CommentAsync(IssueRef issue, string body, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<string>> ListCommentsAsync(IssueRef issue, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task CloseAsync(IssueRef issue, IssueCloseReason reason, CancellationToken cancellationToken) => throw new NotSupportedException();

    private void Link(IssueRef parent, int child)
    {
        List<int> children = _subIssues.TryGetValue(parent.Number, out List<int>? existing) ? existing : _subIssues[parent.Number] = [];
        if (!children.Contains(child))
        {
            children.Add(child);
        }
    }
}
