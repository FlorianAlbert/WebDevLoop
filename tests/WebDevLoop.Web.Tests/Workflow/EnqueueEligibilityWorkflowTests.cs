using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Issues;

namespace WebDevLoop.Web.Tests.Workflow;

public sealed class EnqueueEligibilityWorkflowTests
{
    [Fact]
    public async Task eligible_but_fully_blocked_work_can_be_queued_once_and_queued_again_after_abort()
    {
        using var scenario = new WorkflowScenario(101);
        scenario.Issues.Seed(900, "Parent blocker");
        scenario.Issues.Seed(901, "Ticket blocker");
        scenario.Issues.Seed(100, "Spec", blockedBy: 900);
        scenario.Issues.Seed(101, "Eligible blocked ticket", parent: 100, blockedBy: 901);
        scenario.Issues.Seed(102, "Unlabeled sibling", parent: 100);
        scenario.Issues.SetLabels(102);
        scenario.Issues.SetLabels(100, "WEBDEVLOOP");
        await using var host = new WorkflowHost(scenario);
        using HttpClient client = host.CreateClient();
        var api = new WorkflowApi(client, scenario.Logs);
        int repositoryId = await api.RegisterRepositoryAsync(scenario);

        string runId = await api.EnqueueAsync(repositoryId, 100);
        await api.WaitForStatusAsync(runId, nameof(SpecRunStatus.WaitingForDependency));
        scenario.Issues.IssueReadFailure = new GitHubApiException(GitHubApiErrorKind.Transient, "GitHub unavailable.");
        using HttpResponseMessage duplicate = await client.PostAsJsonAsync(
            $"/api/repos/{repositoryId}/spec-runs", new { specIssueNumber = 100 }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        JsonElement existing = await duplicate.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("AlreadyQueued", existing.GetProperty("outcome").GetString());
        Assert.Equal(runId, existing.GetProperty("runId").GetString());
        Assert.Single((await api.GetAsync($"/api/repos/{repositoryId}/spec-runs")).EnumerateArray());
        Assert.Empty(scenario.Journal.Calls);

        scenario.Issues.IssueReadFailure = null;
        using HttpResponseMessage aborted = await client.PostAsJsonAsync(
            $"/api/spec-runs/{runId}/abort", new { }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, aborted.StatusCode);
        string nextRun = await api.EnqueueAsync(repositoryId, 100);
        Assert.NotEqual(runId, nextRun);
    }

    [Theory]
    [InlineData(false, GitHubApiErrorKind.Unauthorized)]
    [InlineData(false, GitHubApiErrorKind.Forbidden)]
    [InlineData(false, GitHubApiErrorKind.Transient)]
    [InlineData(true, GitHubApiErrorKind.Unauthorized)]
    [InlineData(true, GitHubApiErrorKind.Forbidden)]
    [InlineData(true, GitHubApiErrorKind.Transient)]
    public async Task failed_current_github_reads_do_not_authorize_a_previously_eligible_spec(bool children, GitHubApiErrorKind kind)
    {
        using var scenario = new WorkflowScenario(101);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(101, "Ticket", parent: 100);
        await scenario.Issues.GetSpecGraphAsync(scenario.Issues.Ref(100), TestContext.Current.CancellationToken);
        var failure = new GitHubApiException(kind, $"GitHub lookup failed: {kind}.");
        if (children)
        {
            scenario.Issues.SubIssueReadFailure = failure;
        }
        else
        {
            scenario.Issues.IssueReadFailure = failure;
        }

        await using var host = new WorkflowHost(scenario);
        using HttpClient client = host.CreateClient();
        var api = new WorkflowApi(client, scenario.Logs);
        int repositoryId = await api.RegisterRepositoryAsync(scenario);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/repos/{repositoryId}/spec-runs", new { specIssueNumber = 100 }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Contains(kind.ToString(), problem.GetProperty("detail").GetString());
        Assert.Empty((await api.GetAsync($"/api/repos/{repositoryId}/spec-runs")).EnumerateArray());
    }

    [Theory]
    [InlineData("parent-closed", "open")]
    [InlineData("parent-triage-only", "webdevloop")]
    [InlineData("no-children", "sub-issue")]
    [InlineData("child-closed", "sub-issue")]
    [InlineData("child-unlabeled", "sub-issue")]
    [InlineData("child-triage-only", "sub-issue")]
    [InlineData("only-closed-child-opted-in", "sub-issue")]
    public async Task submission_rechecks_current_parent_and_independent_child_eligibility(string change, string reason)
    {
        using var scenario = new WorkflowScenario(101);
        scenario.Issues.Seed(100, "Spec");
        if (change != "no-children")
        {
            scenario.Issues.Seed(101, "Ticket", parent: 100);
            SpecIssueGraph candidate = await scenario.Issues.GetSpecGraphAsync(scenario.Issues.Ref(100), TestContext.Current.CancellationToken);
            Assert.Equal(IssueState.Open, candidate.Spec.State);
            Assert.Equal(["webdevloop"], candidate.Spec.Labels);
            Assert.Equal(["webdevloop"], Assert.Single(candidate.Tickets).Labels);
        }

        switch (change)
        {
            case "parent-closed":
                await scenario.Issues.CloseAsync(scenario.Issues.Ref(100), IssueCloseReason.Completed, TestContext.Current.CancellationToken);
                break;
            case "parent-triage-only":
                scenario.Issues.SetLabels(100, "ready-for-agent", "ready-for-human");
                break;
            case "child-closed":
                await scenario.Issues.CloseAsync(scenario.Issues.Ref(101), IssueCloseReason.Completed, TestContext.Current.CancellationToken);
                break;
            case "child-unlabeled":
                scenario.Issues.SetLabels(101);
                break;
            case "child-triage-only":
                scenario.Issues.SetLabels(101, "ready-for-agent", "ready-for-human");
                break;
            case "only-closed-child-opted-in":
                scenario.Issues.SetLabels(101);
                scenario.Issues.Seed(102, "Closed opted-in ticket", parent: 100);
                await scenario.Issues.CloseAsync(scenario.Issues.Ref(102), IssueCloseReason.Completed, TestContext.Current.CancellationToken);
                break;
        }

        await using var host = new WorkflowHost(scenario);
        using HttpClient client = host.CreateClient();
        var api = new WorkflowApi(client, scenario.Logs);
        int repositoryId = await api.RegisterRepositoryAsync(scenario);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/repos/{repositoryId}/spec-runs", new { specIssueNumber = 100 }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Contains(reason, problem.GetProperty("detail").GetString());
        Assert.Empty((await api.GetAsync($"/api/repos/{repositoryId}/spec-runs")).EnumerateArray());
    }

    [Fact]
    public async Task an_unlabeled_parent_cannot_be_queued_even_with_an_open_child()
    {
        using var scenario = new WorkflowScenario(101);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(101, "Ticket", parent: 100);
        scenario.Issues.SetLabels(100);
        await using var host = new WorkflowHost(scenario);
        using HttpClient client = host.CreateClient();
        var api = new WorkflowApi(client, scenario.Logs);
        int repositoryId = await api.RegisterRepositoryAsync(scenario);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/repos/{repositoryId}/spec-runs", new { specIssueNumber = 100 }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Contains("webdevloop", problem.GetProperty("detail").GetString());
        Assert.Empty((await api.GetAsync($"/api/repos/{repositoryId}/spec-runs")).EnumerateArray());
    }
}
