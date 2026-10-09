using System.Net;
using System.Text.Json;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Web.Tests.Api;

public sealed class QueueEndpointTests
{
    private static readonly PrerequisiteCheck GitMissing = new("Git CLI", PrerequisiteStatus.Failed, "git not found", "Install git.");

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
    [InlineData(EnqueueOutcome.RepositoryNotFound, HttpStatusCode.NotFound)]
    [InlineData(EnqueueOutcome.ConcurrencyConflict, HttpStatusCode.Conflict)]
    public async Task queueing_failures_map_to_status_codes(EnqueueOutcome outcome, HttpStatusCode expected)
    {
        await using ApiFactory factory = await new ApiFactory().EvaluateAsync();
        factory.Enqueuer.Result = new EnqueueResult(outcome);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostJsonAsync("/api/repos/7/spec-runs", new { specIssueNumber = 10 });

        Assert.Equal(expected, response.StatusCode);
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
