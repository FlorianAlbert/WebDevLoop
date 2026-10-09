using System.Net;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Pulls;
using static WebDevLoop.Infrastructure.Tests.GitHub.Pulls.PullsHarness;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Pulls;

public sealed class GitHubPullRequestTests
{
    private static readonly BranchName Head = new("stack/run-1/t1");
    private static readonly string ListByHead = RepoPath("pulls?head=acme%3Astack%2Frun-1%2Ft1&state=all&per_page=100");
    private static readonly string PullsPath = RepoPath("pulls");

    private readonly PullsHarness _h = new();

    private static DraftPullRequest Draft(string? body = null) =>
        new(Head, Trunk, "Ticket 1", body ?? OwnBody(), Run, Ticket);

    [Fact]
    public async Task Create_draft_posts_exact_head_base_body_and_draft_flag_with_a_pull_request_token()
    {
        _h.Handler
            .Respond(HttpMethod.Get, ListByHead, HttpStatusCode.OK, Array.Empty<object>())
            .Respond(HttpMethod.Post, PullsPath, HttpStatusCode.Created, PullJson(7, Head.Value, "main", OwnBody()));

        PullRequestSnapshot snapshot = await _h.Adapter.CreateDraftPullRequestAsync(Repo, Draft(), CancellationToken.None);

        RecordedCall post = Assert.Single(_h.Handler.CallsTo(HttpMethod.Post, PullsPath));
        Assert.Equal("stack/run-1/t1", post.Json["head"]!.GetValue<string>());
        Assert.Equal("main", post.Json["base"]!.GetValue<string>());
        Assert.Equal("Ticket 1", post.Json["title"]!.GetValue<string>());
        Assert.Equal(OwnBody(), post.Json["body"]!.GetValue<string>());
        Assert.True(post.Json["draft"]!.GetValue<bool>());
        Assert.Equal($"Bearer {Token}", post.Authorization);
        Assert.Equal(2, _h.Tokens.Requests);
        Assert.Equal(7, snapshot.Number.Value);
        Assert.True(snapshot.IsDraft);
        Assert.Equal(PullRequestState.Open, snapshot.State);
        Assert.Equal(Head, snapshot.Head);
        Assert.Equal(Trunk, snapshot.Base);
        Assert.Equal(new CommitSha(Sha('a')), snapshot.HeadSha);
    }

    [Fact]
    public async Task Create_draft_returns_the_existing_pull_request_for_the_same_head_and_identifiers_without_posting()
    {
        _h.Handler.Respond(HttpMethod.Get, ListByHead, HttpStatusCode.OK, new[] { PullJson(7, Head.Value, "main", OwnBody("edited by a human")) });

        PullRequestSnapshot snapshot = await _h.Adapter.CreateDraftPullRequestAsync(Repo, Draft(), CancellationToken.None);

        Assert.Equal(7, snapshot.Number.Value);
        Assert.Empty(_h.Handler.CallsTo(HttpMethod.Post, PullsPath));
    }

    [Fact]
    public async Task Create_draft_fails_when_an_existing_pull_request_for_the_head_carries_other_identifiers()
    {
        string foreign = $"x\n{PullRequestMarker.Format(new RunId("run-9"), Ticket)}";
        _h.Handler.Respond(HttpMethod.Get, ListByHead, HttpStatusCode.OK, new[] { PullJson(7, Head.Value, "main", foreign) });

        var exception = await Assert.ThrowsAsync<PullRequestConflictException>(
            () => _h.Adapter.CreateDraftPullRequestAsync(Repo, Draft(), CancellationToken.None));

        Assert.Equal(7, exception.Existing.Value);
        Assert.Empty(_h.Handler.CallsTo(HttpMethod.Post, PullsPath));
    }

    [Fact]
    public async Task Create_draft_appends_the_run_and_ticket_marker_to_the_body()
    {
        _h.Handler
            .Respond(HttpMethod.Get, ListByHead, HttpStatusCode.OK, Array.Empty<object>())
            .Respond(HttpMethod.Post, PullsPath, HttpStatusCode.Created, PullJson(7, Head.Value, "main", OwnBody()));

        await _h.Adapter.CreateDraftPullRequestAsync(Repo, Draft("Implements ticket"), CancellationToken.None);

        RecordedCall post = Assert.Single(_h.Handler.CallsTo(HttpMethod.Post, PullsPath));
        Assert.Equal(OwnBody(), post.Json["body"]!.GetValue<string>());
    }

    [Fact]
    public async Task Create_draft_rejects_a_body_carrying_another_runs_marker_before_any_request()
    {
        string foreign = $"x\n\n{PullRequestMarker.Format(new RunId("run-9"), Ticket)}";

        await Assert.ThrowsAsync<ArgumentException>(
            () => _h.Adapter.CreateDraftPullRequestAsync(Repo, Draft(foreign), CancellationToken.None));

        Assert.Empty(_h.Handler.Calls);
    }

    [Fact]
    public async Task Create_draft_reconciles_when_github_reports_the_pull_request_already_exists()
    {
        _h.Handler
            .Respond(HttpMethod.Get, ListByHead, HttpStatusCode.OK, Array.Empty<object>())
            .Respond(HttpMethod.Get, ListByHead, HttpStatusCode.OK, new[] { PullJson(7, Head.Value, "main", OwnBody()) })
            .Respond(HttpMethod.Post, PullsPath, HttpStatusCode.UnprocessableEntity, new { message = "Validation Failed" });

        PullRequestSnapshot snapshot = await _h.Adapter.CreateDraftPullRequestAsync(Repo, Draft(), CancellationToken.None);

        Assert.Equal(7, snapshot.Number.Value);
    }

    [Fact]
    public async Task Create_draft_surfaces_api_errors_without_leaking_the_token()
    {
        _h.Handler
            .Respond(HttpMethod.Get, ListByHead, HttpStatusCode.OK, Array.Empty<object>())
            .Respond(HttpMethod.Post, PullsPath, HttpStatusCode.Forbidden, new { message = "Resource not accessible" });

        var exception = await Assert.ThrowsAsync<GitHubApiException>(
            () => _h.Adapter.CreateDraftPullRequestAsync(Repo, Draft(), CancellationToken.None));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Contains("Resource not accessible", exception.Message);
        Assert.DoesNotContain(Token, exception.Message);
    }

    [Fact]
    public async Task Find_by_head_queries_the_exact_head_and_prefers_the_open_pull_request()
    {
        _h.Handler.Respond(HttpMethod.Get, ListByHead, HttpStatusCode.OK, new[]
        {
            PullJson(9, Head.Value, "main", state: "closed"),
            PullJson(5, Head.Value, "main", state: "open"),
            PullJson(8, "stack/run-1/t10", "main", state: "open"),
        });

        PullRequestSnapshot? snapshot = await _h.Adapter.FindPullRequestByHeadAsync(Repo, Head, CancellationToken.None);

        Assert.Equal(5, snapshot!.Number.Value);
    }

    [Fact]
    public async Task Find_by_head_returns_the_newest_pull_request_when_none_is_open_and_null_when_none_exists()
    {
        _h.Handler.Respond(HttpMethod.Get, ListByHead, HttpStatusCode.OK, new[]
        {
            PullJson(3, Head.Value, "main", state: "closed"),
            PullJson(6, Head.Value, "main", state: "closed", merged: true),
        });
        Assert.Equal(6, (await _h.Adapter.FindPullRequestByHeadAsync(Repo, Head, CancellationToken.None))!.Number.Value);

        var empty = new PullsHarness();
        empty.Handler.Respond(HttpMethod.Get, ListByHead, HttpStatusCode.OK, Array.Empty<object>());
        Assert.Null(await empty.Adapter.FindPullRequestByHeadAsync(Repo, Head, CancellationToken.None));
    }

    [Theory]
    [InlineData("open", false, PullRequestState.Open)]
    [InlineData("closed", false, PullRequestState.Closed)]
    [InlineData("closed", true, PullRequestState.Merged)]
    public async Task Get_maps_pull_request_state(string state, bool merged, PullRequestState expected)
    {
        _h.Handler.Respond(HttpMethod.Get, RepoPath("pulls/4"), HttpStatusCode.OK, PullJson(4, Head.Value, "main", draft: false, state: state, merged: merged));

        PullRequestSnapshot snapshot = await _h.Adapter.GetPullRequestAsync(Repo, Pr(4), CancellationToken.None);

        Assert.Equal(expected, snapshot.State);
        Assert.False(snapshot.IsDraft);
    }

    [Fact]
    public async Task Update_base_patches_the_pull_request_base()
    {
        _h.Handler.Respond(HttpMethod.Patch, RepoPath("pulls/4"), HttpStatusCode.OK, PullJson(4, Head.Value, "stack/run-1/t0"));

        await _h.Adapter.UpdatePullRequestBaseAsync(Repo, Pr(4), new BranchName("stack/run-1/t0"), CancellationToken.None);

        RecordedCall patch = Assert.Single(_h.Handler.CallsTo(HttpMethod.Patch, RepoPath("pulls/4")));
        Assert.Equal("stack/run-1/t0", patch.Json["base"]!.GetValue<string>());
    }

    [Fact]
    public async Task Mark_ready_runs_the_graphql_mutation_with_the_pull_request_node_id()
    {
        _h.Handler
            .Respond(HttpMethod.Get, RepoPath("pulls/4"), HttpStatusCode.OK, PullJson(4, Head.Value, "main", draft: true))
            .Respond(HttpMethod.Post, GraphQlPath, HttpStatusCode.OK, new { data = new { markPullRequestReadyForReview = new { pullRequest = new { isDraft = false } } } });

        await _h.Adapter.MarkReadyForReviewAsync(Repo, Pr(4), CancellationToken.None);

        RecordedCall call = Assert.Single(_h.Handler.CallsTo(HttpMethod.Post, GraphQlPath));
        Assert.Contains("markPullRequestReadyForReview", call.Json["query"]!.GetValue<string>());
        Assert.Equal("PR_node_4", call.Json["variables"]!["id"]!.GetValue<string>());
        Assert.Equal($"Bearer {Token}", call.Authorization);
    }

    [Fact]
    public async Task Mark_ready_is_a_no_op_for_a_pull_request_that_is_already_ready()
    {
        _h.Handler.Respond(HttpMethod.Get, RepoPath("pulls/4"), HttpStatusCode.OK, PullJson(4, Head.Value, "main", draft: false));

        await _h.Adapter.MarkReadyForReviewAsync(Repo, Pr(4), CancellationToken.None);

        Assert.Empty(_h.Handler.CallsTo(HttpMethod.Post, GraphQlPath));
    }

    [Fact]
    public async Task Mark_ready_fails_when_graphql_returns_errors()
    {
        _h.Handler
            .Respond(HttpMethod.Get, RepoPath("pulls/4"), HttpStatusCode.OK, PullJson(4, Head.Value, "main", draft: true))
            .Respond(HttpMethod.Post, GraphQlPath, HttpStatusCode.OK, new { errors = new[] { new { message = "Could not resolve" } } });

        var exception = await Assert.ThrowsAsync<GitHubApiException>(
            () => _h.Adapter.MarkReadyForReviewAsync(Repo, Pr(4), CancellationToken.None));

        Assert.Contains("Could not resolve", exception.Message);
    }

    [Fact]
    public async Task Operations_fail_clearly_when_no_token_is_available()
    {
        var h = new PullsHarness(GitHubTokenResult.Unavailable("app not installed"));

        var exception = await Assert.ThrowsAsync<GitHubTokenUnavailableException>(
            () => h.Adapter.GetPullRequestAsync(Repo, Pr(4), CancellationToken.None));

        Assert.Equal("app not installed", exception.Reason);
        Assert.Empty(h.Handler.Calls);
    }
}
