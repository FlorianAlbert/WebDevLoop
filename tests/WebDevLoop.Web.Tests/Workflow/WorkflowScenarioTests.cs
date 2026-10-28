using System.Text.Json;
using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Web.Tests.Workflow;

/// <summary>
/// End-to-end workflow through the real app host: queue a spec over the REST API, implement its ticket DAG concurrently
/// with a continuous frontier, review/fix, integrate into a PR stack, parent review, test, mark ready, observe the human
/// merge, and complete — with GitHub and Copilot faked and git running against a local bare remote.
/// </summary>
public sealed class WorkflowScenarioTests
{
    private const int Spec = 100;
    private const int TicketA = 101;
    private const int TicketB = 102;
    private const int TicketC = 103;
    private const int TicketD = 104;
    private const int TicketE = 105;
    private const int DependentSpec = 200;
    private const int DependentTicket = 201;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task parent_or_blocker_lookup_failure_defers_a_waiting_spec_with_actionable_recovery_and_retries(bool parentUnavailable)
    {
        using var scenario = new WorkflowScenario(TicketA, DependentTicket);
        scenario.Issues.Seed(900, "External blocker");
        scenario.Issues.Seed(Spec, "Waiting spec", parent: null, blockedBy: 900);
        scenario.Issues.Seed(TicketA, "Ticket A", parent: Spec);
        scenario.Issues.Seed(DependentSpec, "Independent spec");
        scenario.Issues.Seed(DependentTicket, "Independent ticket", parent: DependentSpec);
        await using var host = new WorkflowHost(scenario);
        var api = new WorkflowApi(host.CreateClient(), scenario.Logs);
        int repositoryId = await api.RegisterRepositoryAsync(scenario);
        string run = await api.EnqueueAsync(repositoryId, Spec);
        await api.WaitForStatusAsync(run, nameof(SpecRunStatus.WaitingForDependency));
        scenario.Issues.UnavailableIssue = scenario.Issues.Ref(parentUnavailable ? Spec : 900);
        await scenario.Issues.CloseAsync(scenario.Issues.Ref(900), IssueCloseReason.Completed, TestContext.Current.CancellationToken);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        while (!scenario.Logs.Entries.Any(entry => entry.Contains("retry queue recovery", StringComparison.Ordinal)))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), deadline.Token);
        }

        Assert.Equal("WaitingForDependency", (await api.GetAsync($"/api/spec-runs/{run}")).GetProperty("status").GetString());
        Assert.Empty(await api.TicketsAsync(run));
        Assert.Contains(scenario.Logs.Entries, entry => entry.Contains("Check issue access", StringComparison.Ordinal));
        Assert.Contains(scenario.Logs.Entries, entry => entry.Contains(run, StringComparison.Ordinal) && entry.Contains("retry queue recovery", StringComparison.Ordinal));
        string independent = await api.EnqueueAsync(repositoryId, DependentSpec);
        await api.WaitForStatusAsync(independent, nameof(SpecRunStatus.AwaitingMerge));
        Assert.Empty(await api.TicketsAsync(run));
        scenario.Issues.UnavailableIssue = null;

        await api.WaitForStatusAsync(run, nameof(SpecRunStatus.AwaitingMerge));
        Assert.Equal("main", Assert.Single(await api.StackAsync(run)).GetProperty("baseBranch").GetString());
    }

    [Fact]
    public async Task current_external_parent_relationships_hold_work_without_holding_up_independent_specs_and_removal_starts_from_updated_trunk()
    {
        using var scenario = new WorkflowScenario(TicketA, DependentTicket);
        scenario.Issues.Seed(900, "Original external blocker");
        scenario.Issues.Seed(Spec, "Waiting spec", parent: null, blockedBy: 900);
        scenario.Issues.Seed(TicketA, "Ticket A", parent: Spec);
        scenario.Issues.Seed(DependentSpec, "Independent spec");
        scenario.Issues.Seed(DependentTicket, "Independent ticket", parent: DependentSpec);
        var foreign = new IssueRef("elsewhere", "other", 900);
        scenario.Issues.SeedExternal(foreign, "New cross-repository blocker");
        await using var host = new WorkflowHost(scenario);
        var api = new WorkflowApi(host.CreateClient(), scenario.Logs);
        int repositoryId = await api.RegisterRepositoryAsync(scenario);
        string waitingRun = await api.EnqueueAsync(repositoryId, Spec);
        await api.WaitForStatusAsync(waitingRun, nameof(SpecRunStatus.WaitingForDependency));
        scenario.Issues.SetBlockedBy(Spec, foreign);
        await scenario.Issues.CloseAsync(scenario.Issues.Ref(900), IssueCloseReason.Completed, TestContext.Current.CancellationToken);

        string independentRun = await api.EnqueueAsync(repositoryId, DependentSpec);
        JsonElement[] independentStack = await AwaitMergeAndCompleteAsync(api, scenario, independentRun);

        Assert.Equal("WaitingForDependency", (await api.GetAsync($"/api/spec-runs/{waitingRun}")).GetProperty("status").GetString());
        Assert.Empty(await api.TicketsAsync(waitingRun));
        scenario.Issues.SetBlockedBy(Spec);

        JsonElement started = await api.WaitForStatusAsync(waitingRun, nameof(SpecRunStatus.AwaitingMerge));

        Assert.Equal(independentStack[^1].GetProperty("commitSha").GetString(), started.GetProperty("integrationBaseSha").GetString());
        Assert.Equal("main", Assert.Single(await api.StackAsync(waitingRun)).GetProperty("baseBranch").GetString());
    }

    [Fact]
    public async Task diamond_ticket_stack_is_preserved_and_a_closed_unmerged_parent_blocker_releases_an_independent_trunk_based_spec()
    {
        using var scenario = new WorkflowScenario(TicketA, TicketB, TicketC, TicketD, TicketE, DependentTicket);
        SeedDiamondSpec(scenario.Issues);
        scenario.Issues.Seed(DependentSpec, "Dependent spec", parent: null, blockedBy: Spec);
        scenario.Issues.Seed(DependentTicket, "Ticket F", parent: DependentSpec);
        var script = new AgentScript();
        script.CodingStandardsFindingsOnFirstReview.Add(TicketB);
        script.ConcurrentImplementers = new Rendezvous(TicketA, TicketE);
        await using var host = new WorkflowHost(scenario, script);
        var api = new WorkflowApi(host.CreateClient(), scenario.Logs);

        int repositoryId = await api.RegisterRepositoryAsync(scenario);
        string specRun = await api.EnqueueAsync(repositoryId, Spec);
        string dependentRun = await api.EnqueueAsync(repositoryId, DependentSpec);

        await api.WaitForStatusAsync(specRun, nameof(SpecRunStatus.AwaitingMerge));
        await api.WaitForStatusAsync(dependentRun, nameof(SpecRunStatus.WaitingForDependency));
        await scenario.Issues.CloseAsync(scenario.Issues.Ref(Spec), IssueCloseReason.Completed, TestContext.Current.CancellationToken);
        JsonElement dependent = await api.WaitForStatusAsync(dependentRun, nameof(SpecRunStatus.AwaitingMerge));
        Assert.False(dependent.TryGetProperty("dependencyModeUsed", out _));
        Assert.Equal("main", dependent.GetProperty("baseBranch").GetString());

        JsonElement[] stack = await api.StackAsync(specRun);
        JsonElement[] dependentStack = await api.StackAsync(dependentRun);
        Assert.Equal(5, stack.Length);
        Assert.Single(dependentStack);
        Assert.All(stack.Concat(dependentStack), layer => Assert.False(layer.GetProperty("isDraft").GetBoolean()));
        Assert.Equal("main", dependentStack[0].GetProperty("baseBranch").GetString());
        Assert.Equal(
            [.. stack.Select(layer => new PullRequestNumber(layer.GetProperty("pullRequestNumber").GetInt32()))],
            Assert.Single(scenario.Pulls.Stacks()));

        Assert.All(scenario.Pulls.All(), pull => Assert.Equal(PullRequestState.Open, pull.State));
        MergeStack(scenario, dependentStack);
        await api.WaitForStatusAsync(dependentRun, nameof(SpecRunStatus.Completed));

        IReadOnlyList<AgentCall> calls = scenario.Journal.Calls;
        Assert.True(scenario.Journal.PeakImplementers >= 2, $"Implementers never ran concurrently (peak {scenario.Journal.PeakImplementers}).");
        Assert.True(scenario.Journal.PeakImplementers <= 2, $"The per-repository implementer limit of 2 was exceeded (peak {scenario.Journal.PeakImplementers}).");
        AssertStartedAfterBlockersIntegrated(calls, TicketB, TicketA);
        AssertStartedAfterBlockersIntegrated(calls, TicketC, TicketA);
        AssertStartedAfterBlockersIntegrated(calls, TicketD, TicketB, TicketC);
        Assert.Equal(2, calls.Count(call => call.TicketIssue == TicketB && call.Role == AgentRole.Implementer));
        Assert.Contains(calls, call => call.TicketIssue == TicketB && call is { Kind: StepKind.Fix, Resumed: true });
        Assert.Equal(1, calls.Count(call => call.TicketIssue == TicketA && call.Role == AgentRole.Implementer));
        Assert.Contains(calls, call => call.Role == AgentRole.Tester);
        Assert.Contains(calls, call => call.Kind == StepKind.ParentReview);
        Assert.DoesNotContain(calls, call => call.HasGitHubWriteToken);
        Assert.All([TicketA, TicketB, TicketC, TicketD, TicketE, DependentTicket], ticket => Assert.Equal(IssueState.Closed, scenario.Issues.StateOf(ticket)));
        Assert.Equal(6, scenario.Pulls.All().Count);
        Assert.Equal(SpecRunStatus.AwaitingMerge.ToString(), (await api.GetAsync($"/api/spec-runs/{specRun}")).GetProperty("status").GetString());
        Assert.Contains($"ticket-{DependentTicket}.txt", scenario.Sandbox.FilesOnTrunk());
    }

    [Fact]
    public async Task a_lost_github_response_while_publishing_a_layer_is_retried_and_creates_the_pull_request_once()
    {
        using var scenario = new WorkflowScenario(TicketA, TicketB);
        scenario.Issues.Seed(Spec, "Spec");
        scenario.Issues.Seed(TicketA, "Ticket A", parent: Spec);
        scenario.Issues.Seed(TicketB, "Ticket B", parent: Spec, blockedBy: TicketA);
        scenario.Pulls.FailNextCreation();
        await using var host = new WorkflowHost(scenario);
        var api = new WorkflowApi(host.CreateClient(), scenario.Logs);

        int repositoryId = await api.RegisterRepositoryAsync(scenario);
        string specRun = await api.EnqueueAsync(repositoryId, Spec);
        JsonElement[] stack = await AwaitMergeAndCompleteAsync(api, scenario, specRun);

        Assert.Equal(2, stack.Length);
        Assert.Equal(2, scenario.Pulls.All().Count);
        Assert.Equal(2, scenario.Pulls.CreateCalls);
        Assert.Equal(2, scenario.Pulls.All().Select(pull => pull.Head).Distinct().Count());
    }

    [Fact]
    public async Task a_run_interrupted_by_an_app_restart_is_recovered_by_the_next_host_on_the_same_database()
    {
        using var scenario = new WorkflowScenario(TicketA, TicketB, TicketE);
        scenario.Issues.Seed(Spec, "Spec");
        scenario.Issues.Seed(TicketA, "Ticket A", parent: Spec);
        scenario.Issues.Seed(TicketB, "Ticket B", parent: Spec, blockedBy: TicketA);
        scenario.Issues.Seed(TicketE, "Ticket E", parent: Spec);
        var hanging = new AgentScript();
        hanging.HangingImplementers.Add(TicketB);
        string specRun;
        await using (var first = new WorkflowHost(scenario, hanging))
        {
            var api = new WorkflowApi(first.CreateClient(), scenario.Logs);
            int repositoryId = await api.RegisterRepositoryAsync(scenario);
            specRun = await api.EnqueueAsync(repositoryId, Spec);
            await hanging.HangingImplementerStarted.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        }

        await using var second = new WorkflowHost(scenario);
        JsonElement[] stack = await AwaitMergeAndCompleteAsync(new WorkflowApi(second.CreateClient(), scenario.Logs), scenario, specRun);

        Assert.Equal(3, stack.Length);
        AgentCall[] implementersOfB = [.. scenario.Journal.Calls.Where(call => call.TicketIssue == TicketB && call.Role == AgentRole.Implementer)];
        Assert.Equal(2, implementersOfB.Length);
        Assert.True(implementersOfB[1].Resumed, "The restarted host should resume the interrupted implementer session.");
        Assert.Equal(1, scenario.Journal.Calls.Count(call => call.TicketIssue == TicketA && call.Role == AgentRole.Implementer));
    }

    private static void SeedDiamondSpec(FakeGitHubIssues issues)
    {
        issues.Seed(Spec, "Diamond spec");
        issues.Seed(TicketA, "Ticket A", parent: Spec);
        issues.Seed(TicketB, "Ticket B", parent: Spec, blockedBy: TicketA);
        issues.Seed(TicketC, "Ticket C", parent: Spec, blockedBy: TicketA);
        issues.Seed(TicketD, "Ticket D", parent: Spec, blockedBy: [TicketB, TicketC]);
        issues.Seed(TicketE, "Ticket E", parent: Spec);
    }

    private static async Task<JsonElement[]> AwaitMergeAndCompleteAsync(WorkflowApi api, WorkflowScenario scenario, string specRun)
    {
        await api.WaitForStatusAsync(specRun, nameof(SpecRunStatus.AwaitingMerge));
        JsonElement[] stack = await api.StackAsync(specRun);
        MergeStack(scenario, stack);
        await api.WaitForStatusAsync(specRun, nameof(SpecRunStatus.Completed));
        return stack;
    }

    /// <summary>A human merges the stack on GitHub: the PRs are merged and trunk now contains the top layer.</summary>
    private static void MergeStack(WorkflowScenario scenario, JsonElement[] stack)
    {
        scenario.Sandbox.FastForwardTrunk(new CommitSha(stack[^1].GetProperty("commitSha").GetString()!));
        scenario.Pulls.Merge([.. stack.Select(layer => new PullRequestNumber(layer.GetProperty("pullRequestNumber").GetInt32()))]);
    }

    private static void AssertStartedAfterBlockersIntegrated(IReadOnlyList<AgentCall> calls, int ticket, params int[] blockers)
    {
        AgentCall first = calls.First(call => call.TicketIssue == ticket && call.Role == AgentRole.Implementer);
        Assert.All(blockers, blocker => Assert.Contains(blocker, first.ClosedIssuesAtStart));
    }
}
