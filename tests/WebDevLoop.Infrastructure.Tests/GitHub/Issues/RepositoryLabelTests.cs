using System.Net;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Issues;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Issues;

public sealed class RepositoryLabelTests : GitHubIssuesTestBase
{
    private static readonly GitHubRepoRef Repository = new("acme", "widgets");
    private const string LabelsPath = "/repos/acme/widgets/labels";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task lookup_confirms_presence_or_absence_after_reading_all_label_pages(bool exists)
    {
        Api.Get(LabelsPath, new[] { new { name = "ready" } },
            ("Link", "<https://api.github.com/repos/acme/widgets/labels?page=2>; rel=\"next\""));
        Api.Get($"{LabelsPath}?page=2", exists ? [new { name = "webdevloop" }] : Array.Empty<object>());
        var labels = new GitHubRepositoryLabels(new HttpClient(Api), Tokens);

        Assert.Equal(exists, await labels.HasOptInLabelAsync(Repository, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task a_failed_lookup_is_not_confirmed_absence(HttpStatusCode status)
    {
        Api.Respond(HttpMethod.Get, LabelsPath, status, new { message = "Repository access failed" });
        var labels = new GitHubRepositoryLabels(new HttpClient(Api), Tokens);

        RepositoryLabelException error = await Assert.ThrowsAsync<RepositoryLabelException>(() =>
            labels.HasOptInLabelAsync(Repository, TestContext.Current.CancellationToken));

        Assert.Contains("retry", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("access", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task creation_only_provisions_the_fixed_repository_label_and_leaves_existing_issues_untouched()
    {
        Api.Respond(HttpMethod.Post, LabelsPath, HttpStatusCode.Created, new { name = "webdevloop" });
        var labels = new GitHubRepositoryLabels(new HttpClient(Api), Tokens);

        await labels.CreateOptInLabelAsync(Repository, TestContext.Current.CancellationToken);

        IssuesApiRequest request = Assert.Single(Api.Requests);
        Assert.Equal((HttpMethod.Post, LabelsPath, "webdevloop"), (request.Method, request.Path, request.Json.GetProperty("name").GetString()));
        Assert.Equal($"Bearer {TokenValue}", request.Authorization);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task creation_failures_preserve_permission_and_retry_diagnostics(HttpStatusCode status)
    {
        Api.Respond(HttpMethod.Post, LabelsPath, status, new { message = "Label creation failed" });
        var labels = new GitHubRepositoryLabels(new HttpClient(Api), Tokens);

        RepositoryLabelException error = await Assert.ThrowsAsync<RepositoryLabelException>(() =>
            labels.CreateOptInLabelAsync(Repository, TestContext.Current.CancellationToken));

        Assert.Contains("Issues read/write permissions", error.Message);
        Assert.Contains("retry", error.Message);
        Assert.Contains("Label creation failed", error.Message);
    }

    [Fact]
    public async Task a_concurrent_creation_is_successful_only_after_verifying_the_label_now_exists()
    {
        Api.Respond(HttpMethod.Post, LabelsPath, HttpStatusCode.UnprocessableEntity, new { message = "already_exists" });
        Api.Get(LabelsPath, new[] { new { name = "webdevloop" } });
        var labels = new GitHubRepositoryLabels(new HttpClient(Api), Tokens);

        await labels.CreateOptInLabelAsync(Repository, TestContext.Current.CancellationToken);

        Assert.True(await labels.HasOptInLabelAsync(Repository, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task a_rejected_creation_is_not_success_when_the_label_is_still_absent_or_lookup_fails(bool lookupFails)
    {
        Api.Respond(HttpMethod.Post, LabelsPath, HttpStatusCode.UnprocessableEntity, new { message = "validation failed" });
        Api.Respond(HttpMethod.Get, LabelsPath, lookupFails ? HttpStatusCode.Forbidden : HttpStatusCode.OK, Array.Empty<object>());
        var labels = new GitHubRepositoryLabels(new HttpClient(Api), Tokens);

        await Assert.ThrowsAsync<RepositoryLabelException>(() => labels.CreateOptInLabelAsync(Repository, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task a_success_response_with_an_invalid_list_is_not_confirmed_absence()
    {
        Api.Get(LabelsPath, new { message = "not a list" });
        var labels = new GitHubRepositoryLabels(new HttpClient(Api), Tokens);

        await Assert.ThrowsAsync<RepositoryLabelException>(() => labels.HasOptInLabelAsync(Repository, TestContext.Current.CancellationToken));
    }
}
