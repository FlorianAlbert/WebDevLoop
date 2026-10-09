using System.Net;
using System.Text.Json;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.GitHub.Issues;

/// <summary>
/// <see cref="IGitHubIssues"/> over the GitHub REST API (issues, sub-issues, and issue dependencies). Failures surface as
/// <see cref="GitHubApiException"/>; relations are always addressed by database id, never by issue number.
/// </summary>
public sealed class GitHubIssues(HttpClient http, ITokenProvider tokens) : IGitHubIssues
{
    private readonly GitHubRestClient _api = new(http, tokens);

    public async Task<IssueSnapshot> GetIssueAsync(IssueRef issue, CancellationToken cancellationToken)
    {
        GitHubRepoRef repo = RepoOf(issue);
        JsonElement json = await _api.GetAsync(repo, IssuePath(issue), cancellationToken);
        return IssueJson.ToSnapshot(json, repo, await GetBlockedByAsync(issue, cancellationToken));
    }

    public async Task<SpecIssueGraph> GetSpecGraphAsync(IssueRef specIssue, CancellationToken cancellationToken)
    {
        IssueSnapshot spec = await GetIssueAsync(specIssue, cancellationToken);
        IReadOnlyList<IssueSnapshot> tickets = await GetSubIssuesAsync(specIssue, cancellationToken);

        var ticketNumbers = tickets.Select(ticket => ticket.Ref.Number).ToHashSet();
        var ticketByNumber = tickets.ToDictionary(ticket => ticket.Ref.Number);
        var dependencies = tickets
            .SelectMany(ticket => ticket.BlockedBy
                .Where(blocker => IsSameRepo(blocker, specIssue) && ticketNumbers.Contains(blocker.Number))
                .Select(blocker => new DependencyEdge<IssueRef>(ticket.Ref, ticketByNumber[blocker.Number].Ref)))
            .ToList();
        DependencyGraph.EnsureAcyclic(dependencies);

        return new SpecIssueGraph(spec, tickets, dependencies);
    }

    public async Task<IssueSnapshot?> FindFindingIssueAsync(IssueRef specIssue, FindingFingerprint fingerprint, CancellationToken cancellationToken)
    {
        GitHubRepoRef repo = RepoOf(specIssue);
        IReadOnlyList<JsonElement> subIssues = await _api.GetAllAsync(repo, SubIssuesPath(specIssue), cancellationToken);
        foreach (JsonElement subIssue in subIssues.Where(subIssue => FindingFingerprintMarker.IsPresentIn(IssueJson.Body(subIssue), fingerprint)))
        {
            return await ToSnapshotAsync(subIssue, repo, cancellationToken);
        }

        return null;
    }

    /// <summary>
    /// Reuses the finding issue for the fingerprint if one exists. A sub-issue is returned as is; an issue carrying the
    /// fingerprint that is not linked yet (a previous attempt created it and crashed before linking) is linked and returned.
    /// Only a genuinely new finding creates an issue, which is linked to its parent right away.
    /// </summary>
    public async Task<IssueSnapshot> CreateFindingIssueAsync(FindingIssueDraft draft, CancellationToken cancellationToken)
    {
        IssueSnapshot? existing = await FindFindingIssueAsync(draft.Parent, draft.Fingerprint, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        GitHubRepoRef repo = RepoOf(draft.Parent);
        IssueSnapshot? orphan = await FindUnlinkedFindingIssueAsync(draft, repo, cancellationToken);
        if (orphan is not null)
        {
            await AddSubIssueAsync(draft.Parent, orphan.Ref, cancellationToken);
            return orphan;
        }

        string body = $"{draft.Body}\n\n{FindingFingerprintMarker.Render(draft.Fingerprint)}";
        JsonElement created = (await _api.SendAsync(HttpMethod.Post, repo, $"repos/{repo.Owner}/{repo.Name}/issues", new { title = draft.Title, body }, cancellationToken))
            ?? throw new GitHubApiException(GitHubApiErrorKind.Invalid, "GitHub returned no content for the created issue.");
        IssueSnapshot snapshot = IssueJson.ToSnapshot(created, repo, []);

        await AddSubIssueAsync(draft.Parent, snapshot.Ref, cancellationToken);
        return snapshot;
    }

    /// <summary>
    /// Scans repository issues (not only the parent's sub-issues) for the fingerprint. Such an issue can only have been created
    /// after the parent, so listing by <c>since</c> the parent's creation bounds the scan; the list endpoint is strongly consistent, unlike search.
    /// </summary>
    private async Task<IssueSnapshot?> FindUnlinkedFindingIssueAsync(FindingIssueDraft draft, GitHubRepoRef repo, CancellationToken cancellationToken)
    {
        JsonElement parent = await _api.GetAsync(repo, IssuePath(draft.Parent), cancellationToken);
        string query = parent.TryGetProperty("created_at", out JsonElement createdAt) && createdAt.GetString() is { } since
            ? $"state=all&since={since}"
            : "state=all";
        IReadOnlyList<JsonElement> candidates = await _api.GetAllAsync(repo, $"repos/{repo.Owner}/{repo.Name}/issues?{query}", cancellationToken);

        foreach (JsonElement candidate in candidates
            .Where(issue => !issue.TryGetProperty("pull_request", out _) && !HasParent(issue) && FindingFingerprintMarker.IsPresentIn(IssueJson.Body(issue), draft.Fingerprint))
            .OrderBy(issue => issue.GetProperty("number").GetInt32()))
        {
            return await ToSnapshotAsync(candidate, repo, cancellationToken);
        }

        return null;
    }

    private static bool HasParent(JsonElement issue) =>
        issue.TryGetProperty("parent_issue_url", out JsonElement parentUrl) && parentUrl.ValueKind == JsonValueKind.String;

    public async Task AddSubIssueAsync(IssueRef parent, IssueRef child, CancellationToken cancellationToken)
    {
        long childId = await DatabaseIdAsync(child, cancellationToken);
        await AddRelationAsync(
            RepoOf(parent),
            SubIssuesPath(parent),
            new { sub_issue_id = childId, replace_parent = false },
            childId,
            cancellationToken);
    }

    public async Task AddBlockedByAsync(IssueRef blocked, IssueRef blocking, CancellationToken cancellationToken)
    {
        long blockingId = await DatabaseIdAsync(blocking, cancellationToken);
        await AddRelationAsync(
            RepoOf(blocked),
            BlockedByPath(blocked),
            new { issue_id = blockingId },
            blockingId,
            cancellationToken);
    }

    public async Task CommentAsync(IssueRef issue, string body, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        await _api.SendAsync(HttpMethod.Post, RepoOf(issue), $"{IssuePath(issue)}/comments", new { body }, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListCommentsAsync(IssueRef issue, CancellationToken cancellationToken)
    {
        IReadOnlyList<JsonElement> comments = await _api.GetAllAsync(RepoOf(issue), $"{IssuePath(issue)}/comments", cancellationToken);
        return comments.Select(comment => comment.TryGetProperty("body", out JsonElement body) ? body.GetString() ?? string.Empty : string.Empty).ToList();
    }

    public async Task CloseAsync(IssueRef issue, IssueCloseReason reason, CancellationToken cancellationToken)
    {
        GitHubRepoRef repo = RepoOf(issue);
        JsonElement current = await _api.GetAsync(repo, IssuePath(issue), cancellationToken);
        if (IssueJson.ToSnapshot(current, repo, []).State == IssueState.Closed)
        {
            return;
        }

        await _api.SendAsync(
            HttpMethod.Patch,
            repo,
            IssuePath(issue),
            new { state = "closed", state_reason = reason == IssueCloseReason.NotPlanned ? "not_planned" : "completed" },
            cancellationToken);
    }

    /// <summary>GitHub rejects a duplicate relation with 422; that counts as success only if the relation really exists.</summary>
    private async Task AddRelationAsync(GitHubRepoRef repo, string path, object body, long relatedDatabaseId, CancellationToken cancellationToken)
    {
        try
        {
            await _api.SendAsync(HttpMethod.Post, repo, path, body, cancellationToken);
        }
        catch (GitHubApiException error) when (error.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            IReadOnlyList<JsonElement> existing = await _api.GetAllAsync(repo, path, cancellationToken);
            if (!existing.Any(related => related.GetProperty("id").GetInt64() == relatedDatabaseId))
            {
                throw;
            }
        }
    }

    private async Task<long> DatabaseIdAsync(IssueRef issue, CancellationToken cancellationToken)
    {
        if (issue.DatabaseId is { } known)
        {
            return known;
        }

        JsonElement json = await _api.GetAsync(RepoOf(issue), IssuePath(issue), cancellationToken);
        return json.GetProperty("id").GetInt64();
    }

    private async Task<IReadOnlyList<IssueSnapshot>> GetSubIssuesAsync(IssueRef parent, CancellationToken cancellationToken)
    {
        GitHubRepoRef repo = RepoOf(parent);
        IReadOnlyList<JsonElement> subIssues = await _api.GetAllAsync(repo, SubIssuesPath(parent), cancellationToken);

        var snapshots = new List<IssueSnapshot>();
        foreach (JsonElement subIssue in subIssues)
        {
            snapshots.Add(await ToSnapshotAsync(subIssue, repo, cancellationToken));
        }

        return snapshots;
    }

    private async Task<IssueSnapshot> ToSnapshotAsync(JsonElement issue, GitHubRepoRef fallbackRepo, CancellationToken cancellationToken)
    {
        IssueRef issueRef = IssueJson.ToRef(issue, fallbackRepo);
        return IssueJson.ToSnapshot(issue, fallbackRepo, await GetBlockedByAsync(issueRef, cancellationToken));
    }

    private async Task<IReadOnlyList<IssueRef>> GetBlockedByAsync(IssueRef issue, CancellationToken cancellationToken)
    {
        GitHubRepoRef repo = RepoOf(issue);
        IReadOnlyList<JsonElement> blockers = await _api.GetAllAsync(repo, BlockedByPath(issue), cancellationToken);
        return blockers.Select(blocker => IssueJson.ToRef(blocker, repo)).ToList();
    }

    private static GitHubRepoRef RepoOf(IssueRef issue) => new(issue.Owner, issue.Repo);

    private static bool IsSameRepo(IssueRef left, IssueRef right) =>
        string.Equals(left.Owner, right.Owner, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.Repo, right.Repo, StringComparison.OrdinalIgnoreCase);

    private static string IssuePath(IssueRef issue) => $"repos/{issue.Owner}/{issue.Repo}/issues/{issue.Number}";

    private static string SubIssuesPath(IssueRef issue) => $"{IssuePath(issue)}/sub_issues";

    private static string BlockedByPath(IssueRef issue) => $"{IssuePath(issue)}/dependencies/blocked_by";
}
