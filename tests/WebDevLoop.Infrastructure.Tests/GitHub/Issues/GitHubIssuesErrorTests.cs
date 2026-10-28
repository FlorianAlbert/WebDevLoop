using System.Net;
using System.Text.Json;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Issues;
using WebDevLoop.Infrastructure.Tests.GitHub.Auth;
using static WebDevLoop.Infrastructure.Tests.GitHub.Issues.FakeIssuesApiHandler;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Issues;

public sealed class GitHubIssuesErrorTests : GitHubIssuesTestBase
{
    private Task<IssueSnapshot> GetIssue() => CreateSut().GetIssueAsync(Ref(7), CancellationToken.None);

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-repository")]
    [InlineData("https://api.github.com/repos/another-owner")]
    public async Task an_unusable_blocker_repository_is_not_assumed_to_be_the_tickets_repository(string? repositoryUrl)
    {
        Api.Get(IssuePath(7), Issue(7, 700));
        Api.Get(BlockedByPath(7), new[] { new { number = 3, repository_url = repositoryUrl } });

        GitHubApiException error = await Assert.ThrowsAsync<GitHubApiException>(GetIssue);

        Assert.Equal(GitHubApiErrorKind.Invalid, error.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task unavailable_native_blocker_pages_are_errors_not_partial_or_empty_relationships(HttpStatusCode status)
    {
        Api.Get(IssuePath(7), Issue(7, 700));
        Api.Get(BlockedByPath(7) + "?per_page=100", new[] { Issue(3, 300, state: "closed") },
            ("Link", $"<https://api.github.com{BlockedByPath(7)}?per_page=100&page=2>; rel=\"next\""));
        Api.Respond(HttpMethod.Get, BlockedByPath(7) + "?per_page=100&page=2", status, new { message = "Blocker relationships unavailable" });

        GitHubApiException error = await Assert.ThrowsAsync<GitHubApiException>(GetIssue);

        Assert.Equal(status, error.StatusCode);
        Assert.Contains("Blocker relationships unavailable", error.Message);
    }

    [Fact]
    public async Task invalid_json_is_a_useful_github_failure()
    {
        Api.GetRaw(IssuePath(7), "{invalid");

        GitHubApiException error = await Assert.ThrowsAsync<GitHubApiException>(GetIssue);

        Assert.Equal(GitHubApiErrorKind.Invalid, error.Kind);
        Assert.Contains("JSON", error.Message);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("""{"title":"Missing reference","state":"open","labels":[]}""")]
    [InlineData("""{"number":7,"state":"open","labels":[]}""")]
    public async Task incomplete_issue_responses_are_reported_instead_of_leaking_parser_errors(string json)
    {
        Api.GetRaw(IssuePath(7), json);
        Api.Get(BlockedByPath(7), Array.Empty<object>());

        GitHubApiException error = await Assert.ThrowsAsync<GitHubApiException>(GetIssue);

        Assert.Equal(GitHubApiErrorKind.Invalid, error.Kind);
        Assert.Contains("issue", error.Message);
    }

    [Fact]
    public async Task an_unsafe_next_page_link_is_a_failure_not_a_partial_list()
    {
        Api.Get(SubIssuesPath(1), new[] { Issue(2, 200, labels: ["webdevloop"]) },
            ("Link", "<https://other.example/issues?page=2>; rel=\"next\""));

        GitHubApiException error = await Assert.ThrowsAsync<GitHubApiException>(() => CreateSut().GetSubIssuesAsync(Ref(1), CancellationToken.None));

        Assert.Equal(GitHubApiErrorKind.Invalid, error.Kind);
        Assert.Single(Api.Requests);
    }

    [Theory]
    [InlineData("""{"number":7,"title":"Title","state":"unknown","labels":[]}""", "state")]
    [InlineData("""{"number":7,"title":"Title","labels":[]}""", "state")]
    [InlineData("""{"number":7,"title":"Title","state":"open"}""", "labels")]
    [InlineData("""{"number":7,"title":"Title","state":"open","labels":null}""", "labels")]
    [InlineData("""{"number":7,"title":"Title","state":"open","labels":[{"name":null}]}""", "labels")]
    public async Task incomplete_issue_permission_data_is_an_explicit_github_failure(string json, string field)
    {
        Api.Get(IssuePath(7), JsonSerializer.Deserialize<JsonElement>(json));
        Api.Get(BlockedByPath(7), Array.Empty<object>());

        GitHubApiException error = await Assert.ThrowsAsync<GitHubApiException>(GetIssue);

        Assert.Equal(GitHubApiErrorKind.Invalid, error.Kind);
        Assert.Contains(field, error.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task a_failed_later_sub_issue_page_never_returns_a_partial_eligible_list(HttpStatusCode status)
    {
        Api.Get(SubIssuesPath(1) + "?per_page=100", new[] { Issue(2, 200, labels: ["webdevloop"]) },
            ("Link", $"<https://api.github.com{SubIssuesPath(1)}?per_page=100&page=2>; rel=\"next\""));
        Api.Respond(HttpMethod.Get, SubIssuesPath(1) + "?per_page=100&page=2", status, new { message = "Cannot read remaining tickets" });

        GitHubApiException error = await Assert.ThrowsAsync<GitHubApiException>(() => CreateSut().GetSubIssuesAsync(Ref(1), CancellationToken.None));

        Assert.Equal(status, error.StatusCode);
        Assert.Contains("Cannot read remaining tickets", error.Message);
    }

    [Fact]
    public async Task a_non_array_sub_issue_page_is_a_failure_not_an_empty_list()
    {
        Api.Get(SubIssuesPath(1), new { message = "Unexpected response" });

        GitHubApiException error = await Assert.ThrowsAsync<GitHubApiException>(() => CreateSut().GetSubIssuesAsync(Ref(1), CancellationToken.None));

        Assert.Equal(GitHubApiErrorKind.Invalid, error.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task An_unavailable_blocker_relationship_list_never_returns_a_blocker_free_parent(HttpStatusCode status)
    {
        Api.Get(IssuePath(7), Issue(7, 700));
        Api.Respond(HttpMethod.Get, BlockedByPath(7), status, new { message = "Cannot read dependencies" });

        GitHubApiException error = await Assert.ThrowsAsync<GitHubApiException>(GetIssue);

        Assert.Equal(status, error.StatusCode);
        Assert.Contains("Cannot read dependencies", error.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, GitHubApiErrorKind.Transient, true)]
    [InlineData(HttpStatusCode.BadGateway, GitHubApiErrorKind.Transient, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, GitHubApiErrorKind.Transient, true)]
    [InlineData(HttpStatusCode.TooManyRequests, GitHubApiErrorKind.Transient, true)]
    [InlineData(HttpStatusCode.RequestTimeout, GitHubApiErrorKind.Transient, true)]
    [InlineData(HttpStatusCode.Unauthorized, GitHubApiErrorKind.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, GitHubApiErrorKind.Forbidden, false)]
    [InlineData(HttpStatusCode.NotFound, GitHubApiErrorKind.NotFound, false)]
    [InlineData(HttpStatusCode.Conflict, GitHubApiErrorKind.Conflict, false)]
    [InlineData(HttpStatusCode.UnprocessableEntity, GitHubApiErrorKind.Invalid, false)]
    public async Task Http_status_maps_to_an_error_kind_and_retryability(HttpStatusCode status, GitHubApiErrorKind kind, bool retryable)
    {
        Api.Respond(HttpMethod.Get, IssuePath(7), status, new { message = "boom" });

        GitHubApiException error = await Assert.ThrowsAsync<GitHubApiException>(GetIssue);

        Assert.Equal(kind, error.Kind);
        Assert.Equal(retryable, error.IsRetryable);
        Assert.Equal(status, error.StatusCode);
        Assert.Contains("boom", error.Message);
    }

    [Fact]
    public async Task Forbidden_by_the_rate_limit_is_retryable()
    {
        Api.Respond(HttpMethod.Get, IssuePath(7), HttpStatusCode.Forbidden, new { message = "API rate limit exceeded" }, ("X-RateLimit-Remaining", "0"));

        GitHubApiException error = await Assert.ThrowsAsync<GitHubApiException>(GetIssue);

        Assert.True(error.IsRetryable);
    }

    [Fact]
    public async Task Network_failures_are_retryable()
    {
        Api.NetworkFailure = new HttpRequestException("connection reset");

        GitHubApiException error = await Assert.ThrowsAsync<GitHubApiException>(GetIssue);

        Assert.True(error.IsRetryable);
        Assert.IsType<HttpRequestException>(error.InnerException);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_mapped_to_a_domain_error()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Api.NetworkFailure = new TaskCanceledException();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateSut().GetIssueAsync(Ref(7), cancellation.Token));
    }

    [Fact]
    public async Task Unavailable_token_fails_without_calling_the_api_and_is_not_retryable()
    {
        var tokens = new RecordingTokenProvider(GitHubTokenResult.Unavailable("Nobody is signed in to GitHub."));

        GitHubApiException error = await Assert.ThrowsAsync<GitHubApiException>(() => CreateSut(tokens).GetIssueAsync(Ref(7), CancellationToken.None));

        Assert.Equal(GitHubApiErrorKind.Unauthorized, error.Kind);
        Assert.False(error.IsRetryable);
        Assert.Contains("Nobody is signed in to GitHub.", error.Message);
        Assert.Empty(Api.Requests);
    }

    [Fact]
    public async Task Every_request_asks_for_a_fresh_token_and_sends_it_as_bearer()
    {
        Api.Get(IssuePath(7), Issue(7, 700));
        Api.Get(BlockedByPath(7), Array.Empty<object>());
        GitHubIssues sut = CreateSut();

        await sut.GetIssueAsync(Ref(7), CancellationToken.None);
        await sut.GetIssueAsync(Ref(7), CancellationToken.None);

        Assert.Equal(4, Tokens.Requests);
        Assert.All(Api.Requests, request => Assert.Equal($"Bearer {TokenValue}", request.Authorization));
    }
}
