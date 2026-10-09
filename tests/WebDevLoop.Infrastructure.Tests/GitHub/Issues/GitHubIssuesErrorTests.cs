using System.Net;
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
        var tokens = new RecordingTokenProvider(GitHubTokenResult.Unavailable("PAT fallback disabled"));

        GitHubApiException error = await Assert.ThrowsAsync<GitHubApiException>(() => CreateSut(tokens).GetIssueAsync(Ref(7), CancellationToken.None));

        Assert.Equal(GitHubApiErrorKind.Unauthorized, error.Kind);
        Assert.False(error.IsRetryable);
        Assert.Contains("PAT fallback disabled", error.Message);
        Assert.Empty(Api.Requests);
    }

    [Fact]
    public async Task Every_request_asks_for_a_fresh_issues_write_token_for_the_target_repo_and_sends_it_as_bearer()
    {
        Api.Get(IssuePath(7), Issue(7, 700));
        Api.Get(BlockedByPath(7), Array.Empty<object>());
        GitHubIssues sut = CreateSut();

        await sut.GetIssueAsync(Ref(7), CancellationToken.None);
        await sut.GetIssueAsync(Ref(7), CancellationToken.None);

        Assert.Equal(4, Tokens.Requests.Count);
        Assert.All(Tokens.Requests, request =>
        {
            Assert.Equal(new GitHubRepoRef("acme", "widgets"), request.Repo);
            Assert.Equal(GitHubPermissionSet.IssuesWrite, request.Permissions);
        });
        Assert.All(Api.Requests, request => Assert.Equal($"Bearer {TokenValue}", request.Authorization));
    }
}
