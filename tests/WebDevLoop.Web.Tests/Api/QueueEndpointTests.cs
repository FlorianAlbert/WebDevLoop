using System.Net;
using System.Text.Json;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Issues;

namespace WebDevLoop.Web.Tests.Api;

public sealed class QueueEndpointTests
{
    private static readonly PrerequisiteCheck GitMissing = new("Git CLI", PrerequisiteStatus.Failed, "git not found", "Install git.");

    [Fact]
    public async Task readiness_changes_during_a_batch_return_an_unavailable_result_for_each_remaining_selection()
    {
        await using ApiFactory factory = await new ApiFactory().EvaluateAsync();
        factory.Enqueuer.OnEnqueue = async (_, cancellationToken) =>
        {
            factory.Validator.Checks = [GitMissing];
            await factory.Readiness.RefreshAsync(cancellationToken);
            return new EnqueueResult(EnqueueOutcome.Queued, new RunId("run-10"));
        };
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostJsonAsync("/api/repos/7/spec-runs/batch",
            new { specIssueNumbers = new[] { 10, 20, 30 } });
        JsonElement body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["Queued", "Unavailable", "Unavailable"],
            body.EnumerateArray().Select(item => item.GetProperty("outcome").GetString()));
        Assert.Contains("Health page", body[1].GetProperty("message").GetString());
        Assert.Equal((7, 10), Assert.Single(factory.Enqueuer.Calls));
    }

    [Theory]
    [InlineData(EnqueueOutcome.Queued, "Queued")]
    [InlineData(EnqueueOutcome.AlreadyQueued, "AlreadyQueued")]
    [InlineData(EnqueueOutcome.RepositoryNotFound, "RepositoryNotFound")]
    [InlineData(EnqueueOutcome.Ineligible, "Ineligible")]
    [InlineData(EnqueueOutcome.GitHubUnavailable, "GitHubUnavailable")]
    [InlineData(EnqueueOutcome.ConcurrencyConflict, "ConcurrencyConflict")]
    public async Task batch_preserves_string_outcomes_in_the_api_contract(EnqueueOutcome outcome, string expected)
    {
        await using ApiFactory factory = await new ApiFactory().EvaluateAsync();
        factory.Enqueuer.Result = new EnqueueResult(outcome,
            outcome is EnqueueOutcome.Queued or EnqueueOutcome.AlreadyQueued ? new RunId("run-10") : null,
            "Current eligibility could not be established.");
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostJsonAsync("/api/repos/7/spec-runs/batch", new { specIssueNumbers = new[] { 10 } });
        JsonElement body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement item = Assert.Single(body.EnumerateArray());
        Assert.Equal(expected, item.GetProperty("outcome").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new int[0])]
    [InlineData(new[] { 10, 0, 20 })]
    public async Task invalid_batch_does_not_enqueue_any_item(int[]? numbers)
    {
        await using ApiFactory factory = await new ApiFactory().EvaluateAsync();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostJsonAsync("/api/repos/7/spec-runs/batch", new { specIssueNumbers = numbers });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Enqueuer.Calls);
    }

    [Fact]
    public async Task diagnostic_only_mode_rejects_batch_submission_without_any_mutation()
    {
        await using ApiFactory factory = await new ApiFactory().EvaluateAsync(GitMissing);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostJsonAsync("/api/repos/7/spec-runs/batch", new { specIssueNumbers = new[] { 10, 20 } });
        JsonElement body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("DiagnosticOnly", body.GetProperty("mode").GetString());
        Assert.Empty(factory.Enqueuer.Calls);
    }

    [Fact]
    public async Task unexpected_item_failure_is_reported_without_hiding_later_batch_results()
    {
        await using ApiFactory factory = await new ApiFactory().EvaluateAsync();
        factory.Enqueuer.OnEnqueue = (number, _) => number == 10
            ? throw new IOException("Storage unavailable.")
            : Task.FromResult(new EnqueueResult(EnqueueOutcome.Queued, new RunId("run-20")));
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostJsonAsync("/api/repos/7/spec-runs/batch", new { specIssueNumbers = new[] { 10, 20 } });
        JsonElement body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Failed", body[0].GetProperty("outcome").GetString());
        Assert.Contains("Check the queue before retrying", body[0].GetProperty("message").GetString());
        Assert.Equal("Queued", body[1].GetProperty("outcome").GetString());
        Assert.Equal("run-20", body[1].GetProperty("runId").GetString());
    }

    [Theory]
    [InlineData(GitHubApiErrorKind.Unauthorized)]
    [InlineData(GitHubApiErrorKind.Forbidden)]
    [InlineData(GitHubApiErrorKind.Transient)]
    public async Task discovery_returns_actionable_503_when_github_is_unavailable(GitHubApiErrorKind kind)
    {
        await using var factory = new ApiFactory();
        factory.Repositories.Repositories.Add(ApiData.Repository());
        factory.Issues.IssueReadFailure = new GitHubApiException(kind, "GitHub lookup failed.");
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetResponseAsync("/api/repos/1/spec-candidates");
        JsonElement body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Check GitHub access and refresh", body.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("/api/repos/5/spec-candidates", HttpStatusCode.NotFound)]
    [InlineData("/api/repos/5/spec-candidates?page=0", HttpStatusCode.BadRequest)]
    public async Task discovery_validates_repository_and_page(string path, HttpStatusCode expected)
    {
        await using var factory = new ApiFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetResponseAsync(path);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task queueing_a_spec_returns_202_with_the_run_id_when_prerequisites_are_healthy()
    {
        await using ApiFactory factory = await new ApiFactory().EvaluateAsync();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostJsonAsync("/api/repos/7/spec-runs", new { specIssueNumber = 10 });
        JsonElement body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(("run-42", "Queued"), (body.GetProperty("runId").GetString(), body.GetProperty("outcome").GetString()));
        Assert.Equal("/api/spec-runs/run-42", response.Headers.Location!.OriginalString);
        Assert.Equal((7, 10), Assert.Single(factory.Enqueuer.Calls));
    }

    [Fact]
    public async Task diagnostic_only_mode_rejects_queueing_with_the_prerequisite_status()
    {
        await using ApiFactory factory = await new ApiFactory().EvaluateAsync(GitMissing);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostJsonAsync("/api/repos/7/spec-runs", new { specIssueNumber = 10 });
        JsonElement body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("DiagnosticOnly", body.GetProperty("mode").GetString());
        JsonElement failed = Assert.Single(body.GetProperty("failedChecks").EnumerateArray());
        Assert.Equal(("Git CLI", "Install git."), (failed.GetProperty("name").GetString(), failed.GetProperty("remediation").GetString()));
        Assert.Empty(factory.Enqueuer.Calls);
    }

    [Fact]
    public async Task queueing_is_rejected_until_prerequisites_were_evaluated()
    {
        await using var factory = new ApiFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostJsonAsync("/api/repos/7/spec-runs", new { specIssueNumber = 10 });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(factory.Enqueuer.Calls);
    }

    [Fact]
    public async Task queueing_an_already_queued_spec_returns_200_with_the_existing_run()
    {
        await using ApiFactory factory = await new ApiFactory().EvaluateAsync();
        factory.Enqueuer.Result = new EnqueueResult(EnqueueOutcome.AlreadyQueued, new RunId("run-9"));
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostJsonAsync("/api/repos/7/spec-runs", new { specIssueNumber = 10 });
        JsonElement body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("run-9", "AlreadyQueued"), (body.GetProperty("runId").GetString(), body.GetProperty("outcome").GetString()));
    }

    [Theory]
    [InlineData(EnqueueOutcome.RepositoryNotFound, HttpStatusCode.NotFound, "This repository no longer exists. Select a connected repository.")]
    [InlineData(EnqueueOutcome.ConcurrencyConflict, HttpStatusCode.Conflict, "The queue changed concurrently. Refresh the queue and retry.")]
    [InlineData(EnqueueOutcome.Ineligible, HttpStatusCode.Conflict, "Current GitHub eligibility could not be established.")]
    [InlineData(EnqueueOutcome.GitHubUnavailable, HttpStatusCode.ServiceUnavailable, "Current GitHub eligibility could not be established.")]
    public async Task queueing_failures_share_batch_messages_and_preserve_status_codes(
        EnqueueOutcome outcome, HttpStatusCode expected, string expectedMessage)
    {
        await using ApiFactory factory = await new ApiFactory().EvaluateAsync();
        factory.Enqueuer.Result = new EnqueueResult(outcome, Reason: "Current GitHub eligibility could not be established.");
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostJsonAsync("/api/repos/7/spec-runs", new { specIssueNumber = 10 });
        using HttpResponseMessage batchResponse = await client.PostJsonAsync("/api/repos/7/spec-runs/batch", new { specIssueNumbers = new[] { 10 } });
        JsonElement problem = await response.ReadJsonAsync();
        JsonElement batch = await batchResponse.ReadJsonAsync();

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, batchResponse.StatusCode);
        Assert.Equal(expectedMessage, problem.GetProperty("detail").GetString());
        Assert.Equal(expectedMessage, Assert.Single(batch.EnumerateArray()).GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task a_non_positive_issue_number_is_a_validation_error(int issueNumber)
    {
        await using ApiFactory factory = await new ApiFactory().EvaluateAsync();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostJsonAsync("/api/repos/7/spec-runs", new { specIssueNumber = issueNumber });
        JsonElement body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(body.GetProperty("errors").TryGetProperty("SpecIssueNumber", out _));
        Assert.Empty(factory.Enqueuer.Calls);
    }

    [Fact]
    public async Task the_queue_of_a_repository_is_listed_in_queue_order()
    {
        await using var factory = new ApiFactory();
        factory.Repositories.Repositories.Add(ApiData.Repository(1));
        factory.Runs.SpecRuns.AddRange([ApiData.SpecRun("run-1", 1, SpecRunStatus.Preparing, 1), ApiData.SpecRun("run-2", 1, SpecRunStatus.Queued, 2), ApiData.SpecRun("other", 2)]);
        using HttpClient client = factory.CreateClient();

        JsonElement list = await client.GetJsonAsync("/api/repos/1/spec-runs");

        Assert.Equal(["run-1", "run-2"], list.EnumerateArray().Select(item => item.GetProperty("id").GetString()));
        Assert.Equal("Preparing", list[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task listing_the_queue_of_an_unknown_repository_returns_404()
    {
        await using var factory = new ApiFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetResponseAsync("/api/repos/5/spec-runs");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
