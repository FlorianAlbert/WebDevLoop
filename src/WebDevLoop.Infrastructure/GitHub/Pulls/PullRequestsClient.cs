using System.Net;
using System.Text.Json.Nodes;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.GitHub.Pulls;

internal sealed class PullRequestsClient(GitHubApiClient api)
{
    private const string MarkReadyMutation =
        "mutation($id: ID!) { markPullRequestReadyForReview(input: {pullRequestId: $id}) { pullRequest { id number isDraft } } }";

    public async Task<PullRequestDto?> FindByHeadAsync(GitHubRepoRef repo, BranchName head, CancellationToken cancellationToken)
    {
        string filter = Uri.EscapeDataString($"{repo.Owner}:{head}");
        GitHubApiResponse response = (await api.SendAsync(
            HttpMethod.Get, repo, $"pulls?head={filter}&state=all&per_page=100", null, cancellationToken)).EnsureSuccess();

        IEnumerable<PullRequestDto> exact = response.Read<PullRequestDto[]>().Where(pull => pull.Head.Ref == head.Value);
        return exact.OrderByDescending(pull => pull.PullState == PullRequestState.Open).ThenByDescending(pull => pull.Number).FirstOrDefault();
    }

    public async Task<PullRequestDto> GetAsync(GitHubRepoRef repo, PullRequestNumber number, CancellationToken cancellationToken) =>
        (await api.SendAsync(HttpMethod.Get, repo, $"pulls/{number}", null, cancellationToken)).EnsureSuccess().Read<PullRequestDto>();

    /// <summary>Reconciles by exact head ref and run/ticket marker before creating; never creates a second PR for one head.</summary>
    public async Task<PullRequestDto> CreateDraftAsync(GitHubRepoRef repo, DraftPullRequest request, CancellationToken cancellationToken)
    {
        (RunId RunId, TicketRunId TicketRunId) marker = (request.RunId, request.TicketRunId);
        string body = BodyWithMarker(request);

        PullRequestDto? existing = await FindByHeadAsync(repo, request.Head, cancellationToken);
        if (existing is not null)
        {
            return ReuseIfOwned(existing, request.Head, marker);
        }

        GitHubApiResponse created = await api.SendAsync(
            HttpMethod.Post,
            repo,
            "pulls",
            new { title = request.Title, head = request.Head.Value, @base = request.Base.Value, body, draft = true },
            cancellationToken);

        if (created.IsSuccess)
        {
            return created.Read<PullRequestDto>();
        }

        // A concurrent creator (or a crash after the POST) may have created it in between; reconcile before failing.
        if (created.Status == HttpStatusCode.UnprocessableEntity
            && await FindByHeadAsync(repo, request.Head, cancellationToken) is { } raced)
        {
            return ReuseIfOwned(raced, request.Head, marker);
        }

        throw created.ToException();
    }

    public async Task UpdateBaseAsync(GitHubRepoRef repo, PullRequestNumber number, BranchName newBase, CancellationToken cancellationToken) =>
        (await api.SendAsync(HttpMethod.Patch, repo, $"pulls/{number}", new { @base = newBase.Value }, cancellationToken)).EnsureSuccess();

    public async Task MarkReadyAsync(GitHubRepoRef repo, PullRequestNumber number, CancellationToken cancellationToken)
    {
        PullRequestDto pull = await GetAsync(repo, number, cancellationToken);
        if (!pull.Draft)
        {
            return;
        }

        await api.GraphQlAsync(repo, MarkReadyMutation, new { id = pull.NodeId }, cancellationToken);
    }

    /// <summary>
    /// True when <paramref name="commit"/> is reachable from <paramref name="branch"/> (compare says identical or ahead). The compare
    /// endpoint needs the App's <c>contents:read</c>; any non-success response (including 403/404 on private repositories) is an error, never "not contained".
    /// </summary>
    public async Task<bool> BranchContainsAsync(GitHubRepoRef repo, BranchName branch, string commit, CancellationToken cancellationToken)
    {
        string branchPath = string.Join('/', branch.Value.Split('/').Select(Uri.EscapeDataString));
        GitHubApiResponse response = await api.SendAsync(
            HttpMethod.Get, repo, $"compare/{commit}...{branchPath}", null, cancellationToken);

        string? status = JsonNode.Parse(response.EnsureSuccess().Content)?["status"]?.GetValue<string>();
        return status is "ahead" or "identical";
    }

    /// <summary>The body with this run's marker appended; a body already carrying it is kept, one carrying another marker is rejected.</summary>
    private static string BodyWithMarker(DraftPullRequest request)
    {
        (RunId RunId, TicketRunId TicketRunId)? existing = PullRequestMarker.TryParse(request.Body);
        if (existing is null)
        {
            return $"{request.Body}\n\n{PullRequestMarker.Format(request.RunId, request.TicketRunId)}";
        }

        return existing == (request.RunId, request.TicketRunId)
            ? request.Body
            : throw new ArgumentException("The pull request body carries the marker of another run or ticket.", nameof(request));
    }

    private static PullRequestDto ReuseIfOwned(PullRequestDto existing, BranchName head, (RunId RunId, TicketRunId TicketRunId) expected) =>
        PullRequestMarker.TryParse(existing.Body) == expected
            ? existing
            : throw new PullRequestConflictException(head, new PullRequestNumber(existing.Number));
}
