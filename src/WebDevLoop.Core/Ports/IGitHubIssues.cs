using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

/// <summary>
/// GitHub issue reads and mutations. Reads return current state and labels or throw <see cref="GitHubIssuesException"/>;
/// unavailable information must not be replaced with stale snapshots. Only the app calls this port; agents never mutate issues.
/// </summary>
public interface IGitHubIssues
{
    Task<ParentIssuePage> ListOpenParentIssuesAsync(GitHubRepoRef repository, int page, CancellationToken cancellationToken);

    Task<IssueSnapshot> GetIssueAsync(IssueRef issue, CancellationToken cancellationToken);

    /// <summary>Current sub-issues across all pages, including closed issues; does not validate their dependency graph.</summary>
    Task<IReadOnlyList<IssueSnapshot>> GetSubIssuesAsync(IssueRef parent, CancellationToken cancellationToken);

    Task<SpecIssueGraph> GetSpecGraphAsync(IssueRef specIssue, CancellationToken cancellationToken);

    /// <summary>Finds an already-created finding sub-issue by its embedded fingerprint (crash recovery / dedupe).</summary>
    Task<IssueSnapshot?> FindFindingIssueAsync(IssueRef specIssue, FindingFingerprint fingerprint, CancellationToken cancellationToken);

    /// <summary>
    /// Creates an opted-in finding and links it to its parent, or reuses the fingerprint's existing issue (including an
    /// unlinked issue left by an interrupted attempt). Reuse never changes labels or reverses a maintainer's opt-out.
    /// Failed creation, opt-in, or linking throws; retrying must not duplicate an issue already created.
    /// </summary>
    Task<IssueSnapshot> CreateFindingIssueAsync(FindingIssueDraft draft, CancellationToken cancellationToken);

    /// <summary>Idempotent: linking an existing sub-issue again succeeds.</summary>
    Task AddSubIssueAsync(IssueRef parent, IssueRef child, CancellationToken cancellationToken);

    /// <summary>Idempotent: adding an existing blocked-by relation again succeeds.</summary>
    Task AddBlockedByAsync(IssueRef blocked, IssueRef blocking, CancellationToken cancellationToken);

    Task CommentAsync(IssueRef issue, string body, CancellationToken cancellationToken);

    /// <summary>Bodies of all comments on the issue, oldest first (lets the app check its own markers before commenting again).</summary>
    Task<IReadOnlyList<string>> ListCommentsAsync(IssueRef issue, CancellationToken cancellationToken);

    /// <summary>Idempotent: closing an already-closed issue succeeds.</summary>
    Task CloseAsync(IssueRef issue, IssueCloseReason reason, CancellationToken cancellationToken);
}
