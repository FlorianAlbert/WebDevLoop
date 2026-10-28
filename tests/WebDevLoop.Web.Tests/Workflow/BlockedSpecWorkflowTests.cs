using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Frontier;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Issues;

namespace WebDevLoop.Web.Tests.Workflow;

public sealed class BlockedSpecWorkflowTests
{
    [Theory]
    [InlineData("parent unlabeled", false, "opted out of WebDevLoop")]
    [InlineData("parent closed", false, "is closed")]
    [InlineData("tickets unlabeled", false, "No eligible work")]
    [InlineData("parent unlabeled", true, "opted out of WebDevLoop")]
    public async Task parked_work_that_opts_out_finishes_while_all_slots_are_busy_without_resuming(
        string change, bool hasIntegratedWork, string expectedReason)
    {
        using var scenario = new WorkflowScenario(101, 102, 201);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(900, "Blocker");
        scenario.Issues.Seed(101, "Ticket", parent: 100, blockedBy: 900);
        if (hasIntegratedWork)
        {
            scenario.Issues.Seed(102, "Integrated ticket", parent: 100);
        }
        else
        {
            scenario.Issues.Seed(102, "Other blocked ticket", parent: 100, blockedBy: 900);
        }
        scenario.Issues.Seed(200, "Busy spec");
        scenario.Issues.Seed(201, "Busy ticket", parent: 200);
        var script = new AgentScript();
        script.HangingImplementers.Add(201);
        await using var host = new WorkflowHost(scenario, script);
        var api = new WorkflowApi(host.CreateClient(), scenario.Logs);
        int repository = await api.RegisterRepositoryAsync(scenario);
        string run = await api.EnqueueAsync(repository, 100);
        var parked = await api.WaitForStatusAsync(run, nameof(SpecRunStatus.WaitingForTickets));
        string busy = await api.EnqueueAsync(repository, 200);
        await script.HangingImplementerStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        using (IServiceScope scope = host.Services.CreateScope())
        {
            Assert.Null(await scope.ServiceProvider.GetRequiredService<SpecQueueScheduler>()
                .FindFreeSlotAsync(repository, TestContext.Current.CancellationToken));
        }
        if (change == "parent closed")
        {
            await scenario.Issues.CloseAsync(scenario.Issues.Ref(100), IssueCloseReason.Completed, TestContext.Current.CancellationToken);
        }
        else if (change == "parent unlabeled")
        {
            scenario.Issues.SetLabels(100);
        }
        else
        {
            scenario.Issues.SetLabels(101);
            scenario.Issues.SetLabels(102);
            scenario.Issues.SetBlockedBy(100, scenario.Issues.Ref(900));
        }
        var finished = await api.WaitForStatusAsync(run, nameof(SpecRunStatus.Completed), TimeSpan.FromSeconds(10));

        Assert.Contains(expectedReason, finished.GetProperty("workDecisionReason").GetString());
        Assert.Equal(parked.GetProperty("integrationBranch").GetString(), finished.GetProperty("integrationBranch").GetString());
        Assert.Equal(parked.GetProperty("integrationTipSha").GetString(), finished.GetProperty("integrationTipSha").GetString());
        Assert.Equal(0, finished.GetProperty("reviewCycle").GetInt32());
        Assert.Equal(0, finished.GetProperty("testCycle").GetInt32());
        Assert.Equal(hasIntegratedWork ? 1 : 0, (await api.StackAsync(run)).Length);
        Assert.Equal(nameof(SpecRunStatus.Running), (await api.GetAsync($"/api/spec-runs/{busy}")).GetProperty("status").GetString());
        Assert.DoesNotContain(scenario.Journal.Calls, call => call.TicketIssue == 101 || call.Kind == StepKind.ParentReview || call.Role == AgentRole.Tester);
        using (IServiceScope scope = host.Services.CreateScope())
        {
            SpecScheduleResult result = await scope.ServiceProvider.GetRequiredService<SpecQueueScheduler>()
                .ScheduleRepositoryAsync(repository, TestContext.Current.CancellationToken);
            Assert.Empty(result.Activated);
        }
        script.ReleaseHangingImplementer.TrySetResult();
        await api.WaitForStatusAsync(busy, nameof(SpecRunStatus.AwaitingMerge));
    }

    [Fact]
    public async Task agent_capacity_exhaustion_does_not_park_a_spec_with_runnable_work()
    {
        using var scenario = new WorkflowScenario(101, 201, 202);
        scenario.Issues.Seed(100, "Busy spec");
        scenario.Issues.Seed(101, "Busy ticket", parent: 100);
        scenario.Issues.Seed(200, "Capacity-limited spec");
        scenario.Issues.Seed(201, "Runnable ticket", parent: 200);
        scenario.Issues.Seed(900, "External blocker");
        scenario.Issues.Seed(202, "Blocked ticket", parent: 200, blockedBy: 900);
        var script = new AgentScript();
        script.HangingImplementers.Add(101);
        await using var host = new WorkflowHost(scenario, script);
        using var client = host.CreateClient();
        var api = new WorkflowApi(client, scenario.Logs);
        int repository = await api.RegisterRepositoryAsync(scenario);
        using var settings = await client.PutAsJsonAsync($"/api/repos/{repository}/settings",
            new { maxActiveSpecsPerRepo = 2, maxConcurrentImplementersPerRepo = 1 },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        string busy = await api.EnqueueAsync(repository, 100);
        await script.HangingImplementerStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        string limited = await api.EnqueueAsync(repository, 200);
        await api.WaitForStatusAsync(limited, nameof(SpecRunStatus.Running));
        using (IServiceScope scope = host.Services.CreateScope())
        {
            FrontierResult result = await scope.ServiceProvider.GetRequiredService<FrontierService>()
                .ReconcileAsync(new RunId(limited), TestContext.Current.CancellationToken);
            Assert.Empty(result.Dispatched);
        }

        await api.WaitForTicketStatusAsync(limited, 201, nameof(TicketRunStatus.Ready));
        Assert.Equal(nameof(SpecRunStatus.Running), (await api.GetAsync($"/api/spec-runs/{limited}")).GetProperty("status").GetString());
        Assert.DoesNotContain(scenario.Journal.Calls, call => call.TicketIssue == 201);
        script.ReleaseHangingImplementer.TrySetResult();
        await api.WaitForStatusAsync(busy, nameof(SpecRunStatus.AwaitingMerge));
        await api.WaitForStatusAsync(limited, nameof(SpecRunStatus.WaitingForTickets));
        await api.WaitForTicketStatusAsync(limited, 201, nameof(TicketRunStatus.Integrated));
    }

    [Fact]
    public async Task parked_decisions_use_fresh_relationships_fail_closed_on_unknown_blockers_and_refresh_the_visible_reason()
    {
        using var scenario = new WorkflowScenario(101);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(900, "Original blocker");
        scenario.Issues.Seed(101, "Ticket", parent: 100, blockedBy: 900);
        var foreign = new IssueRef("elsewhere", "library", 901);
        scenario.Issues.Seed(foreign, IssueState.Open);
        await using var host = new WorkflowHost(scenario);
        var api = new WorkflowApi(host.CreateClient(), scenario.Logs);
        int repository = await api.RegisterRepositoryAsync(scenario);
        string run = await api.EnqueueAsync(repository, 100);
        await api.WaitForStatusAsync(run, nameof(SpecRunStatus.WaitingForTickets));
        scenario.Issues.SetBlockedBy(101, foreign);
        await scenario.Issues.CloseAsync(scenario.Issues.Ref(900), IssueCloseReason.Completed, TestContext.Current.CancellationToken);
        scenario.Issues.FailReads(foreign, new GitHubApiException(GitHubApiErrorKind.Transient, "Blocker state unknown; retry."));

        using (IServiceScope scope = host.Services.CreateScope())
        {
            SpecScheduleResult deferred = await scope.ServiceProvider.GetRequiredService<SpecQueueScheduler>()
                .ScheduleRepositoryAsync(repository, TestContext.Current.CancellationToken);
            Assert.Empty(deferred.Activated);
            Assert.Contains("Blocker state unknown", Assert.Single(deferred.Faults).Error);
        }
        var unknown = await api.GetAsync($"/api/spec-runs/{run}");
        Assert.Equal(nameof(SpecRunStatus.WaitingForTickets), unknown.GetProperty("status").GetString());
        Assert.Contains("Blocker state unknown", unknown.GetProperty("workDecisionReason").GetString());
        scenario.Issues.FailReads(foreign, null);
        using (IServiceScope scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SpecQueueScheduler>()
                .ScheduleRepositoryAsync(repository, TestContext.Current.CancellationToken);
        }
        var confirmedBlocked = await api.GetAsync($"/api/spec-runs/{run}");
        Assert.Equal(nameof(SpecRunStatus.WaitingForTickets), confirmedBlocked.GetProperty("status").GetString());
        Assert.Contains("open native blocker", confirmedBlocked.GetProperty("workDecisionReason").GetString());
        Assert.DoesNotContain(scenario.Journal.Calls, call => call.Role == AgentRole.Implementer);

        scenario.Issues.SetBlockedBy(101);
        await api.WaitForStatusAsync(run, nameof(SpecRunStatus.AwaitingMerge));
        Assert.Single(await api.StackAsync(run));
    }

    [Fact]
    public async Task selected_work_finishes_before_parking_and_restart_resumes_the_same_partial_branch_without_preemption()
    {
        using var scenario = new WorkflowScenario(101, 102, 201);
        scenario.Issues.Seed(100, "Partial spec");
        scenario.Issues.Seed(101, "Selected ticket", parent: 100);
        scenario.Issues.Seed(900, "External blocker");
        scenario.Issues.Seed(102, "Later ticket", parent: 100, blockedBy: 900);
        scenario.Issues.Seed(200, "Independent spec");
        scenario.Issues.Seed(201, "Independent ticket", parent: 200);
        var firstScript = new AgentScript();
        firstScript.HangingImplementers.Add(101);
        string run;
        string branch;
        string tip;
        string ticket;
        int repository;
        await using (var first = new WorkflowHost(scenario, firstScript))
        {
            var api = new WorkflowApi(first.CreateClient(), scenario.Logs);
            repository = await api.RegisterRepositoryAsync(scenario);
            run = await api.EnqueueAsync(repository, 100);
            await firstScript.HangingImplementerStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            using (IServiceScope scope = first.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<FrontierService>()
                    .ReconcileAsync(new RunId(run), TestContext.Current.CancellationToken);
            }
            Assert.Equal(nameof(SpecRunStatus.Running), (await api.GetAsync($"/api/spec-runs/{run}")).GetProperty("status").GetString());
            firstScript.ReleaseHangingImplementer.TrySetResult();
            var parked = await api.WaitForStatusAsync(run, nameof(SpecRunStatus.WaitingForTickets));
            Assert.Equal(0, parked.GetProperty("reviewCycle").GetInt32());
            Assert.Equal(0, parked.GetProperty("testCycle").GetInt32());
            branch = parked.GetProperty("integrationBranch").GetString()!;
            tip = parked.GetProperty("integrationTipSha").GetString()!;
            ticket = (await api.TicketsAsync(run)).Single(item => item.GetProperty("issueNumber").GetInt32() == 102).GetProperty("id").GetString()!;
            Assert.Single(await api.StackAsync(run));
            Assert.DoesNotContain(scenario.Journal.Calls, call => call.Kind == StepKind.ParentReview || call.Role == AgentRole.Tester);
        }

        var secondScript = new AgentScript();
        secondScript.HangingImplementers.Add(201);
        await using var second = new WorkflowHost(scenario, secondScript);
        var resumedApi = new WorkflowApi(second.CreateClient(), scenario.Logs);
        await resumedApi.WaitForStatusAsync(run, nameof(SpecRunStatus.WaitingForTickets));
        string independent = await resumedApi.EnqueueAsync(repository, 200);
        await secondScript.HangingImplementerStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await scenario.Issues.CloseAsync(scenario.Issues.Ref(900), IssueCloseReason.Completed, TestContext.Current.CancellationToken);
        using (IServiceScope scope = second.Services.CreateScope())
        {
            SpecScheduleResult result = await scope.ServiceProvider.GetRequiredService<SpecQueueScheduler>()
                .ScheduleRepositoryAsync(repository, TestContext.Current.CancellationToken);
            Assert.Empty(result.Activated);
        }
        var stillParked = await resumedApi.GetAsync($"/api/spec-runs/{run}");
        Assert.Equal(nameof(SpecRunStatus.WaitingForTickets), stillParked.GetProperty("status").GetString());
        Assert.Equal(tip, stillParked.GetProperty("integrationTipSha").GetString());
        Assert.Equal(nameof(SpecRunStatus.Running), (await resumedApi.GetAsync($"/api/spec-runs/{independent}")).GetProperty("status").GetString());

        secondScript.ReleaseHangingImplementer.TrySetResult();
        await resumedApi.WaitForStatusAsync(independent, nameof(SpecRunStatus.AwaitingMerge));
        var finished = await resumedApi.WaitForStatusAsync(run, nameof(SpecRunStatus.AwaitingMerge), TimeSpan.FromSeconds(15));

        Assert.Equal(branch, finished.GetProperty("integrationBranch").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, finished.GetProperty("workDecisionReason").ValueKind);
        Assert.Equal(ticket, (await resumedApi.TicketsAsync(run)).Single(item => item.GetProperty("issueNumber").GetInt32() == 102).GetProperty("id").GetString());
        Assert.Equal(2, (await resumedApi.StackAsync(run)).Length);
        Assert.Single(scenario.Journal.Calls, call => call.Role == AgentRole.Implementer && call.TicketIssue == 101);
        Assert.Single(scenario.Journal.Calls, call => call.Role == AgentRole.Implementer && call.TicketIssue == 102);
        Assert.Equal(2, scenario.Journal.Calls.Count(call => call.Role == AgentRole.Explorer));
    }

    [Fact]
    public async Task fully_blocked_spec_releases_its_slot_for_an_independent_queued_spec()
    {
        using var scenario = new WorkflowScenario(101, 201);
        scenario.Issues.Seed(100, "Blocked spec");
        scenario.Issues.Seed(900, "External blocker");
        scenario.Issues.Seed(101, "Blocked ticket", parent: 100, blockedBy: 900);
        scenario.Issues.Seed(200, "Independent spec");
        scenario.Issues.Seed(201, "Independent ticket", parent: 200);
        await using var host = new WorkflowHost(scenario);
        var api = new WorkflowApi(host.CreateClient(), scenario.Logs);
        int repository = await api.RegisterRepositoryAsync(scenario);
        string blocked = await api.EnqueueAsync(repository, 100);
        string independent = await api.EnqueueAsync(repository, 200);

        var waiting = await api.WaitForStatusAsync(blocked, nameof(SpecRunStatus.WaitingForTickets), TimeSpan.FromSeconds(10));

        Assert.Contains("open native blocker", waiting.GetProperty("workDecisionReason").GetString());
        Assert.Equal(0, waiting.GetProperty("reviewCycle").GetInt32());
        await api.WaitForStatusAsync(independent, nameof(SpecRunStatus.AwaitingMerge));
        Assert.Empty(await api.StackAsync(blocked));
        Assert.DoesNotContain(scenario.Journal.Calls, call => call.TicketIssue == 101);
    }
}
