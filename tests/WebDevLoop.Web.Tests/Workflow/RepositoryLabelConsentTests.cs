using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Web.Tests.Workflow;

public sealed class RepositoryLabelConsentTests
{
    [Fact]
    public async Task approval_resumes_parent_review_after_the_last_selected_ticket_finishes_without_the_label()
    {
        using var scenario = new WorkflowScenario(101);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(101, "Selected ticket", parent: 100);
        var script = new AgentScript();
        script.HangingImplementers.Add(101);
        await using var host = new WorkflowHost(scenario, script);
        using HttpClient client = host.CreateClient();
        var api = new WorkflowApi(client, scenario.Logs);
        int repositoryId = await api.RegisterRepositoryAsync(scenario, Path.Combine(scenario.Sandbox.DataDirectory, "workspaces", "r"));
        string run = await api.EnqueueAsync(repositoryId, 100);
        await script.HangingImplementerStarted.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        scenario.Labels.Exists = false;
        script.ReleaseHangingImplementer.TrySetResult();
        await api.WaitForTicketStatusAsync(run, 101, nameof(TicketRunStatus.Integrated));
        await Task.Delay(TimeSpan.FromSeconds(1.5), TestContext.Current.CancellationToken);
        Assert.Equal("Running", (await api.GetAsync($"/api/spec-runs/{run}")).GetProperty("status").GetString());
        Assert.DoesNotContain(scenario.Journal.Calls, call => call.Kind == StepKind.ParentReview);

        using HttpResponseMessage approved = await client.PostAsJsonAsync($"/api/repos/{repositoryId}/opt-in",
            new { approveLabelCreation = true }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        await api.WaitForStatusAsync(run, nameof(SpecRunStatus.AwaitingMerge), TimeSpan.FromSeconds(15));
        Assert.Single(scenario.Journal.Calls, call => call.TicketIssue == 101 && call.Role == AgentRole.Implementer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task an_existing_connection_with_a_missing_or_unavailable_label_does_not_start_queued_work(bool lookupFails)
    {
        using var scenario = new WorkflowScenario(101);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(101, "Ticket", parent: 100);
        await using var host = new WorkflowHost(scenario);
        using HttpClient client = host.CreateClient();
        var api = new WorkflowApi(client, scenario.Logs);
        int repositoryId = await api.RegisterRepositoryAsync(scenario, Path.Combine(scenario.Sandbox.DataDirectory, "workspaces", "r"));
        scenario.Labels.Exists = false;
        scenario.Labels.FailLookup = lookupFails;

        string run = await api.EnqueueAsync(repositoryId, 100);
        await Task.Delay(TimeSpan.FromSeconds(1.5), TestContext.Current.CancellationToken);

        Assert.Equal("Queued", (await api.GetAsync($"/api/spec-runs/{run}")).GetProperty("status").GetString());
        Assert.Empty(scenario.Journal.Calls);
        Assert.False(scenario.Labels.Exists);
        Assert.True((await api.GetAsync($"/api/repos/{repositoryId}")).GetProperty("isEnabled").GetBoolean());
        scenario.Labels.FailLookup = false;
        using HttpResponseMessage approved = await client.PostAsJsonAsync($"/api/repos/{repositoryId}/opt-in",
            new { approveLabelCreation = true }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        await api.WaitForStatusAsync(run, nameof(SpecRunStatus.AwaitingMerge));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task deleted_or_unavailable_labels_pause_new_tickets_but_selected_work_finishes_and_approval_resumes(bool lookupFails)
    {
        using var scenario = new WorkflowScenario(101, 102);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(101, "Selected ticket", parent: 100);
        scenario.Issues.Seed(102, "Next ticket", parent: 100, blockedBy: 101);
        var script = new AgentScript();
        script.HangingImplementers.Add(101);
        await using var host = new WorkflowHost(scenario, script);
        using HttpClient client = host.CreateClient();
        var api = new WorkflowApi(client, scenario.Logs);
        int repositoryId = await api.RegisterRepositoryAsync(scenario, Path.Combine(scenario.Sandbox.DataDirectory, "workspaces", "r"));
        string run = await api.EnqueueAsync(repositoryId, 100);
        await script.HangingImplementerStarted.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        scenario.Labels.Exists = false;
        scenario.Labels.FailLookup = lookupFails;
        using HttpResponseMessage paused = await client.GetAsync($"/api/repos/{repositoryId}/opt-in", TestContext.Current.CancellationToken);
        Assert.Equal(lookupFails ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Conflict, paused.StatusCode);
        script.ReleaseHangingImplementer.TrySetResult();
        await api.WaitForTicketStatusAsync(run, 101, nameof(TicketRunStatus.Integrated));
        await Task.Delay(TimeSpan.FromSeconds(1.5), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(scenario.Journal.Calls, call => call.TicketIssue == 102 && call.Role == AgentRole.Implementer);
        JsonElement repository = await api.GetAsync($"/api/repos/{repositoryId}");
        Assert.True(repository.GetProperty("isEnabled").GetBoolean());
        Assert.Equal(scenario.Sandbox.RemotePath, repository.GetProperty("cloneUrl").GetString());
        using HttpResponseMessage declined = await client.PostAsJsonAsync($"/api/repos/{repositoryId}/opt-in",
            new { approveLabelCreation = false }, TestContext.Current.CancellationToken);
        Assert.Equal(lookupFails ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Conflict, declined.StatusCode);
        Assert.False(scenario.Labels.Exists);

        scenario.Labels.FailLookup = false;
        using HttpResponseMessage approved = await client.PostAsJsonAsync($"/api/repos/{repositoryId}/opt-in",
            new { approveLabelCreation = true }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        await api.WaitForStatusAsync(run, nameof(SpecRunStatus.AwaitingMerge));
        Assert.True(scenario.Labels.Exists);
        Assert.Single(scenario.Journal.Calls, call => call.TicketIssue == 101 && call.Role == AgentRole.Implementer);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task existing_or_explicitly_approved_labels_allow_connection(bool exists, bool approval)
    {
        using var scenario = new WorkflowScenario();
        scenario.Labels.Exists = exists;
        await using var host = new WorkflowHost(scenario);
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/repos",
            new { owner = "acme", name = "widgets", approveLabelCreation = approval }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(scenario.Labels.Exists);
        Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/repos", TestContext.Current.CancellationToken)).EnumerateArray());
    }

    [Theory]
    [InlineData(true, false, "check")]
    [InlineData(false, true, "create")]
    public async Task label_failures_report_retry_errors_and_leave_the_repository_disconnected(bool lookupFails, bool creationFails, string operation)
    {
        using var scenario = new WorkflowScenario();
        scenario.Labels.Exists = false;
        scenario.Labels.FailLookup = lookupFails;
        scenario.Labels.FailCreation = creationFails;
        await using var host = new WorkflowHost(scenario);
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/repos",
            new { owner = "acme", name = "widgets", approveLabelCreation = true }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        string problem = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains(operation, problem);
        Assert.Contains("permissions", problem);
        Assert.Contains("retry", problem);
        Assert.False(scenario.Labels.Exists);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/repos", TestContext.Current.CancellationToken)).EnumerateArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task registration_without_approval_does_not_create_a_missing_label_or_connect(bool? approval)
    {
        using var scenario = new WorkflowScenario();
        scenario.Labels.Exists = false;
        await using var host = new WorkflowHost(scenario);
        using HttpClient client = host.CreateClient();

        object command = approval is null
            ? new { owner = "acme", name = "widgets" }
            : new { owner = "acme", name = "widgets", approveLabelCreation = approval.Value };
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/repos", command, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("approval", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.OrdinalIgnoreCase);
        Assert.False(scenario.Labels.Exists);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/repos", TestContext.Current.CancellationToken)).EnumerateArray());
    }

    [Fact]
    public async Task duplicate_registration_preserves_existing_configuration_and_does_not_provision_labels()
    {
        using var scenario = new WorkflowScenario();
        await using var host = new WorkflowHost(scenario);
        using HttpClient client = host.CreateClient();
        var api = new WorkflowApi(client, scenario.Logs);
        int repositoryId = await api.RegisterRepositoryAsync(scenario);
        scenario.Labels.Exists = false;

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/repos",
            new { owner = "acme", name = "widgets", defaultBaseBranch = "develop", approveLabelCreation = true },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.False(scenario.Labels.Exists);
        JsonElement repository = await api.GetAsync($"/api/repos/{repositoryId}");
        Assert.Equal("main", repository.GetProperty("defaultBaseBranch").GetString());
        Assert.Equal(scenario.Sandbox.RemotePath, repository.GetProperty("cloneUrl").GetString());
        Assert.Single((await api.GetAsync("/api/repos")).EnumerateArray());
    }
}
