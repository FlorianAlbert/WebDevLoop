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

    [Fact]
    public async Task diamond_and_independent_tickets_are_implemented_reviewed_stacked_and_completed_after_the_merge_including_a_stacked_dependent_spec()
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
        await api.UseDependencyModeAsync(repositoryId, nameof(SpecDependencyMode.StackOnTop));
        string specRun = await api.EnqueueAsync(repositoryId, Spec);
        string dependentRun = await api.EnqueueAsync(repositoryId, DependentSpec);

        await api.WaitForStatusAsync(specRun, nameof(SpecRunStatus.AwaitingMerge));
        JsonElement dependent = await api.WaitForStatusAsync(dependentRun, nameof(SpecRunStatus.AwaitingMerge));
        Assert.Equal(nameof(SpecDependencyMode.StackOnTop), dependent.GetProperty("dependencyModeUsed").GetString());

        JsonElement[] stack = await api.StackAsync(specRun);
        JsonElement[] dependentStack = await api.StackAsync(dependentRun);
        Assert.Equal(5, stack.Length);
        Assert.Single(dependentStack);
        Assert.All(stack.Concat(dependentStack), layer => Assert.False(layer.GetProperty("isDraft").GetBoolean()));
        Assert.Equal(stack[^1].GetProperty("branchName").GetString(), dependentStack[0].GetProperty("baseBranch").GetString());
        Assert.Equal(
            [.. stack.Concat(dependentStack).Select(layer => new PullRequestNumber(layer.GetProperty("pullRequestNumber").GetInt32()))],
            Assert.Single(scenario.Pulls.Stacks()));

        MergeStack(scenario, stack);
        await api.WaitForStatusAsync(specRun, nameof(SpecRunStatus.Completed));
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
        Assert.All(scenario.Pulls.All(), pull => Assert.Equal(PullRequestState.Merged, pull.State));
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
