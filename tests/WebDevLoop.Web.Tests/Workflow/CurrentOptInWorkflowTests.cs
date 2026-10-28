using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Frontier;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Issues;

namespace WebDevLoop.Web.Tests.Workflow;

public sealed class CurrentOptInWorkflowTests
{
    [Theory]
    [InlineData("parent unlabeled", false, "requires the 'webdevloop' label")]
    [InlineData("parent closed", false, "must be open")]
    [InlineData("tickets unlabeled", false, "at least one open sub-issue")]
    [InlineData("parent unlabeled", true, "requires the 'webdevloop' label")]
    [InlineData("parent closed", true, "must be open")]
    [InlineData("tickets unlabeled", true, "at least one open sub-issue")]
    public async Task opt_out_before_start_defers_without_preparation_or_agents_and_leaves_the_slot_for_independent_work(
        string change, bool waitingForDependency, string expectedReason)
    {
        using var scenario = new WorkflowScenario(101, 102, 201);
        scenario.Issues.Seed(900, "Parent blocker");
        scenario.Issues.Seed(100, "Spec", parent: null, blockedBy: 900);
        scenario.Issues.Seed(101, "First ticket", parent: 100);
        scenario.Issues.Seed(102, "Second ticket", parent: 100);
        scenario.Issues.Seed(200, "Independent spec");
        scenario.Issues.Seed(201, "Independent ticket", parent: 200);
        await using var host = new WorkflowHost(scenario);
        using var client = host.CreateClient();
        var api = new WorkflowApi(client, scenario.Logs);
        int repository = await api.RegisterRepositoryAsync(scenario);
        if (!waitingForDependency)
        {
            using var disabled = await client.PatchAsJsonAsync($"/api/repos/{repository}", new { isEnabled = false }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        }

        string run = await api.EnqueueAsync(repository, 100);
        string waitingStatus = waitingForDependency ? nameof(SpecRunStatus.WaitingForDependency) : nameof(SpecRunStatus.Queued);
        await api.WaitForStatusAsync(run, waitingStatus);
        if (change == "parent closed")
        {
            await scenario.Issues.CloseAsync(scenario.Issues.Ref(100), IssueCloseReason.Completed, TestContext.Current.CancellationToken);
        }
        else if (change == "parent unlabeled")
        {
            scenario.Issues.SetLabels(100, "ready-for-agent");
        }
        else
        {
            scenario.Issues.SetLabels(101, "ready-for-agent");
            scenario.Issues.SetLabels(102, "ready-for-agent");
        }

        await scenario.Issues.CloseAsync(scenario.Issues.Ref(900), IssueCloseReason.Completed, TestContext.Current.CancellationToken);
        using var enabled = await client.PatchAsJsonAsync($"/api/repos/{repository}", new { isEnabled = true }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        using (IServiceScope scope = host.Services.CreateScope())
        {
            var scheduler = scope.ServiceProvider.GetRequiredService<SpecQueueScheduler>();
            SpecScheduleResult result = await scheduler.ScheduleRepositoryAsync(repository, TestContext.Current.CancellationToken);
            Assert.Empty(result.Activated);
            Assert.Equal(1, await scheduler.FindFreeSlotAsync(repository, TestContext.Current.CancellationToken));
        }

        var deferred = await api.GetAsync($"/api/spec-runs/{run}");
        Assert.Equal(waitingStatus, deferred.GetProperty("status").GetString());
        Assert.Contains(expectedReason, deferred.GetProperty("workDecisionReason").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, deferred.GetProperty("startedAt").ValueKind);
        Assert.Empty(await api.TicketsAsync(run));
        Assert.Empty(scenario.Journal.Calls);
        var repositoryView = await api.GetAsync($"/api/repos/{repository}");
        Assert.False(Directory.Exists(repositoryView.GetProperty("localPath").GetString()));

        string independent = await api.EnqueueAsync(repository, 200);
        await api.WaitForStatusAsync(independent, nameof(SpecRunStatus.AwaitingMerge));
        Assert.Equal(waitingStatus, (await api.GetAsync($"/api/spec-runs/{run}")).GetProperty("status").GetString());
        Assert.Empty(await api.TicketsAsync(run));
        Assert.DoesNotContain(scenario.Journal.Calls, call => call.TicketIssue is 101 or 102);
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("sub-issues")]
    public async Task unavailable_start_eligibility_defers_with_a_visible_retry_reason_and_recovers(string unavailable)
    {
        using var scenario = new WorkflowScenario(101);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(101, "Ticket", parent: 100);
        await using var host = new WorkflowHost(scenario);
        using var client = host.CreateClient();
        var api = new WorkflowApi(client, scenario.Logs);
        int repository = await api.RegisterRepositoryAsync(scenario);
        using var disabled = await client.PatchAsJsonAsync($"/api/repos/{repository}", new { isEnabled = false }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        string run = await api.EnqueueAsync(repository, 100);
        var failure = new GitHubApiException(GitHubApiErrorKind.Transient, "Current permission is unavailable.");
        if (unavailable == "parent")
        {
            scenario.Issues.FailReads(scenario.Issues.Ref(100), failure);
        }
        else
        {
            scenario.Issues.SubIssueReadFailure = failure;
        }

        using var enabled = await client.PatchAsJsonAsync($"/api/repos/{repository}", new { isEnabled = true }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        using (IServiceScope scope = host.Services.CreateScope())
        {
            var scheduler = scope.ServiceProvider.GetRequiredService<SpecQueueScheduler>();
            SpecScheduleResult result = await scheduler.ScheduleRepositoryAsync(repository, TestContext.Current.CancellationToken);
            Assert.Empty(result.Activated);
            Assert.Contains("Current permission is unavailable", Assert.Single(result.Faults).Error);
            Assert.Equal(1, await scheduler.FindFreeSlotAsync(repository, TestContext.Current.CancellationToken));
        }

        var deferred = await api.GetAsync($"/api/spec-runs/{run}");
        Assert.Equal(nameof(SpecRunStatus.Queued), deferred.GetProperty("status").GetString());
        Assert.Contains("Current permission is unavailable", deferred.GetProperty("workDecisionReason").GetString());
        Assert.Contains("retry queue recovery", deferred.GetProperty("workDecisionReason").GetString());
        Assert.Empty(await api.TicketsAsync(run));
        Assert.Empty(scenario.Journal.Calls);

        scenario.Issues.FailReads(scenario.Issues.Ref(100), null);
        scenario.Issues.SubIssueReadFailure = null;
        var finished = await api.WaitForStatusAsync(run, nameof(SpecRunStatus.AwaitingMerge));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, finished.GetProperty("workDecisionReason").ValueKind);
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("sub-issues")]
    [InlineData("ticket")]
    public async Task failed_current_queries_defer_completion_after_selected_work_finishes_and_recover(string unavailable)
    {
        using var scenario = new WorkflowScenario(101, 102);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(101, "Selected ticket", parent: 100);
        scenario.Issues.Seed(102, "Excluded ticket", parent: 100);
        scenario.Issues.SetLabels(102, "ready-for-agent");
        var script = new AgentScript();
        script.HangingImplementers.Add(101);
        await using var host = new WorkflowHost(scenario, script);
        var api = new WorkflowApi(host.CreateClient(), scenario.Logs);
        int repository = await api.RegisterRepositoryAsync(scenario);
        string run = await api.EnqueueAsync(repository, 100);
        await script.HangingImplementerStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var failure = new GitHubApiException(GitHubApiErrorKind.Transient, "Fresh permission is unavailable; retry.");
        if (unavailable == "sub-issues")
        {
            scenario.Issues.SubIssueReadFailure = failure;
        }
        else
        {
            scenario.Issues.FailReads(scenario.Issues.Ref(unavailable == "parent" ? 100 : 102), failure);
        }

        scenario.Issues.SetLabels(101);
        script.ReleaseHangingImplementer.TrySetResult();
        await api.WaitForTicketStatusAsync(run, 101, nameof(TicketRunStatus.Integrated));
        using (IServiceScope scope = host.Services.CreateScope())
        {
            FrontierResult result = await scope.ServiceProvider.GetRequiredService<FrontierService>()
                .ReconcileAsync(new RunId(run), TestContext.Current.CancellationToken);
            Assert.Equal(FrontierOutcome.GitHubUnavailable, result.Outcome);
        }

        var deferred = await api.GetAsync($"/api/spec-runs/{run}");
        Assert.Equal(nameof(SpecRunStatus.Running), deferred.GetProperty("status").GetString());
        Assert.Contains("Fresh permission is unavailable", deferred.GetProperty("workDecisionReason").GetString());
        Assert.DoesNotContain(scenario.Journal.Calls, call => call.Kind == StepKind.ParentReview || call.Role == AgentRole.Tester || call.TicketIssue == 102);

        scenario.Issues.SubIssueReadFailure = null;
        scenario.Issues.FailReads(scenario.Issues.Ref(100), null);
        scenario.Issues.FailReads(scenario.Issues.Ref(102), null);
        var finished = await api.WaitForStatusAsync(run, nameof(SpecRunStatus.AwaitingMerge));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, finished.GetProperty("workDecisionReason").ValueKind);
        Assert.Single(await api.StackAsync(run));
    }

    [Fact]
    public async Task no_eligible_work_completes_without_review_or_pull_requests_and_requires_explicit_requeue()
    {
        using var scenario = new WorkflowScenario(101);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(900, "Blocker");
        scenario.Issues.Seed(101, "Ticket", parent: 100, blockedBy: 900);
        await using var host = new WorkflowHost(scenario);
        var api = new WorkflowApi(host.CreateClient(), scenario.Logs);
        int repository = await api.RegisterRepositoryAsync(scenario);
        string run = await api.EnqueueAsync(repository, 100);
        await api.WaitForStatusAsync(run, nameof(SpecRunStatus.WaitingForTickets));

        scenario.Issues.SetLabels(101);
        var finished = await api.WaitForStatusAsync(run, nameof(SpecRunStatus.Completed));

        Assert.Contains("No eligible work", finished.GetProperty("workDecisionReason").GetString());
        Assert.Equal(0, finished.GetProperty("reviewCycle").GetInt32());
        Assert.Equal(0, finished.GetProperty("testCycle").GetInt32());
        Assert.Empty(await api.StackAsync(run));
        Assert.Empty(scenario.Pulls.All());
        Assert.DoesNotContain(scenario.Journal.Calls, call => call.Role != AgentRole.Explorer);

        scenario.Issues.SetLabels(101, WebDevLoopOptIn.Label);
        string requeued = await api.EnqueueAsync(repository, 100);
        Assert.NotEqual(run, requeued);
        Assert.Equal(nameof(SpecRunStatus.Completed), (await api.GetAsync($"/api/spec-runs/{run}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task parent_opt_out_finishes_selected_work_stops_new_work_and_preserves_the_stack_for_explicit_requeue()
    {
        using var scenario = new WorkflowScenario(101, 102);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(101, "Selected ticket", parent: 100);
        scenario.Issues.Seed(900, "Blocker");
        scenario.Issues.Seed(102, "Later ticket", parent: 100, blockedBy: 900);
        var script = new AgentScript();
        script.HangingImplementers.Add(101);
        await using var host = new WorkflowHost(scenario, script);
        var api = new WorkflowApi(host.CreateClient(), scenario.Logs);
        int repository = await api.RegisterRepositoryAsync(scenario);
        string run = await api.EnqueueAsync(repository, 100);
        await script.HangingImplementerStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        scenario.Issues.SetLabels(100);
        scenario.Issues.SetLabels(101);
        await scenario.Issues.CloseAsync(scenario.Issues.Ref(900), IssueCloseReason.Completed, TestContext.Current.CancellationToken);
        using (IServiceScope scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<FrontierService>().ReconcileAsync(new RunId(run), TestContext.Current.CancellationToken);
        }

        script.ReleaseHangingImplementer.TrySetResult();
        var stopped = await api.WaitForStatusAsync(run, nameof(SpecRunStatus.Completed));
        Assert.Contains("opted out", stopped.GetProperty("workDecisionReason").GetString());
        await api.WaitForTicketStatusAsync(run, 101, nameof(TicketRunStatus.Integrated));
        var layer = Assert.Single(await api.StackAsync(run));
        Assert.NotNull(scenario.Sandbox.RemoteTip(new BranchName(layer.GetProperty("branchName").GetString()!)));
        Assert.Single(scenario.Pulls.All());
        var mergeStatus = await api.GetAsync($"/api/spec-runs/{run}/merge-status");
        Assert.Equal("Stopped", mergeStatus.GetProperty("state").GetString());
        Assert.Contains("opted out", mergeStatus.GetProperty("detail").GetString());
        Assert.DoesNotContain(scenario.Journal.Calls, call => call.TicketIssue == 102 || call.Role == AgentRole.Tester);
        Assert.DoesNotContain(scenario.Journal.Calls, call => call.Kind == StepKind.ParentReview);

        scenario.Issues.SetLabels(100, WebDevLoopOptIn.Label);
        string requeued = await api.EnqueueAsync(repository, 100);
        Assert.NotEqual(run, requeued);
        Assert.Equal(nameof(SpecRunStatus.Completed), (await api.GetAsync($"/api/spec-runs/{run}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task frontier_uses_current_independent_opt_in_not_triage_labels()
    {
        using var scenario = new WorkflowScenario(101, 102);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(101, "Selected ticket", parent: 100);
        scenario.Issues.Seed(900, "Blocker");
        scenario.Issues.Seed(102, "Later ticket", parent: 100, blockedBy: 900);
        var script = new AgentScript();
        script.HangingImplementers.Add(101);
        await using var host = new WorkflowHost(scenario, script);
        var api = new WorkflowApi(host.CreateClient(), scenario.Logs);
        int repository = await api.RegisterRepositoryAsync(scenario);
        string run = await api.EnqueueAsync(repository, 100);
        await script.HangingImplementerStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        scenario.Issues.SetLabels(102, "ready-for-agent");
        await scenario.Issues.CloseAsync(scenario.Issues.Ref(900), IssueCloseReason.Completed, TestContext.Current.CancellationToken);
        using IServiceScope scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<FrontierService>().ReconcileAsync(new RunId(run), TestContext.Current.CancellationToken);

        Assert.Equal(nameof(TicketRunStatus.Blocked), (await api.TicketsAsync(run)).Single(ticket => ticket.GetProperty("issueNumber").GetInt32() == 102).GetProperty("status").GetString());
        Assert.DoesNotContain(scenario.Journal.Calls, call => call.Role == AgentRole.Implementer && call.TicketIssue == 102);
    }
}
