using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Issues;

namespace WebDevLoop.Web.Tests.Workflow;

public sealed class FindingOptInWorkflowTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(StepKind.ParentReview)]
    [InlineData(StepKind.Test)]
    public async Task new_findings_are_opted_in_linked_to_the_parent_and_implemented_as_normal_work(StepKind source)
    {
        using var scenario = new WorkflowScenario(101);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(101, "Ticket", parent: 100);
        await using var host = new WorkflowHost(scenario, new AgentScript { FindingOnFirstCycle = source });
        var api = new WorkflowApi(host.CreateClient(), scenario.Logs);
        int repository = await api.RegisterRepositoryAsync(scenario);

        string run = await api.EnqueueAsync(repository, 100);
        await api.WaitForStatusAsync(run, nameof(SpecRunStatus.AwaitingMerge));

        IssueSnapshot finding = Assert.Single(
            await scenario.Issues.GetSubIssuesAsync(scenario.Issues.Ref(100), TestContext.Current.CancellationToken),
            issue => issue.Ref.Number != 101);
        Assert.Equal(["webdevloop"], finding.Labels);
        Assert.Equal(IssueState.Closed, finding.State);
        Assert.Equal(2, (await api.TicketsAsync(run)).Length);
        Assert.Contains(scenario.Journal.Calls, call => call.Role == AgentRole.Implementer && call.TicketIssue == finding.Ref.Number);
    }

    [Theory]
    [InlineData(StepKind.ParentReview)]
    [InlineData(StepKind.Test)]
    public async Task repeated_findings_reuse_the_ticket_without_reversing_a_maintainers_opt_out(StepKind source)
    {
        using var scenario = new WorkflowScenario(101);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(101, "Ticket", parent: 100);
        var script = new AgentScript { FindingOnFirstCycle = source, RepeatFinding = true };
        script.HangingImplementers.Add(102);
        await using var host = new WorkflowHost(scenario, script);
        var api = new WorkflowApi(host.CreateClient(), scenario.Logs);
        int repository = await api.RegisterRepositoryAsync(scenario);
        string run = await api.EnqueueAsync(repository, 100);
        await script.HangingImplementerStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        scenario.Issues.SetLabels(102, "ready-for-human");
        script.ReleaseHangingImplementer.TrySetResult();
        await api.WaitForStatusAsync(run, nameof(SpecRunStatus.NeedsAttention));

        IssueSnapshot finding = await scenario.Issues.GetIssueAsync(scenario.Issues.Ref(102), Ct);
        Assert.Equal(["ready-for-human"], finding.Labels);
        Assert.Equal(2, (await api.TicketsAsync(run)).Length);
        Assert.Equal(2, (await scenario.Issues.GetSubIssuesAsync(scenario.Issues.Ref(100), Ct)).Count);
        Assert.Single(scenario.Journal.Calls, call => call.Role == AgentRole.Implementer && call.TicketIssue == 102);
    }

    [Theory]
    [InlineData(StepKind.ParentReview, GitHubApiErrorKind.Unauthorized)]
    [InlineData(StepKind.ParentReview, GitHubApiErrorKind.Forbidden)]
    [InlineData(StepKind.ParentReview, GitHubApiErrorKind.Transient)]
    [InlineData(StepKind.Test, GitHubApiErrorKind.Unauthorized)]
    [InlineData(StepKind.Test, GitHubApiErrorKind.Forbidden)]
    [InlineData(StepKind.Test, GitHubApiErrorKind.Transient)]
    public async Task failed_finding_creation_does_not_advance_the_phase_and_a_restart_retries_once(StepKind source, GitHubApiErrorKind kind)
    {
        using var scenario = new WorkflowScenario(101);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(101, "Ticket", parent: 100);
        scenario.Issues.FindingCreationFailure = new GitHubApiException(kind, $"Finding opt-in failed: {kind}");
        string run;
        await using (var first = new WorkflowHost(scenario, new AgentScript { FindingOnFirstCycle = source }))
        {
            var api = new WorkflowApi(first.CreateClient(), scenario.Logs);
            int repository = await api.RegisterRepositoryAsync(scenario);
            run = await api.EnqueueAsync(repository, 100);
            await scenario.Issues.FindingIssuanceFailed.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);

            Assert.Equal(source == StepKind.Test ? "Testing" : "ParentReviewing",
                (await api.GetAsync($"/api/spec-runs/{run}")).GetProperty("status").GetString());
            Assert.Single(await api.TicketsAsync(run));
            Assert.Single(await scenario.Issues.GetSubIssuesAsync(scenario.Issues.Ref(100), Ct));
        }

        Assert.Contains(scenario.Logs.Entries, entry => entry.Contains($"Finding opt-in failed: {kind}", StringComparison.Ordinal));
        scenario.Issues.FindingCreationFailure = null;
        await using var second = new WorkflowHost(scenario);
        var recoveredApi = new WorkflowApi(second.CreateClient(), scenario.Logs);
        await recoveredApi.WaitForStatusAsync(run, nameof(SpecRunStatus.AwaitingMerge));

        Assert.Equal(2, (await recoveredApi.TicketsAsync(run)).Length);
        Assert.Equal(["webdevloop"], (await scenario.Issues.GetIssueAsync(scenario.Issues.Ref(102), Ct)).Labels);
        Assert.Equal(2, (await scenario.Issues.GetSubIssuesAsync(scenario.Issues.Ref(100), Ct)).Count);
        Assert.Single(scenario.Journal.Calls, call => call.Role == AgentRole.Implementer && call.TicketIssue == 102);
    }

    [Theory]
    [InlineData(StepKind.ParentReview, false)]
    [InlineData(StepKind.ParentReview, true)]
    [InlineData(StepKind.Test, false)]
    [InlineData(StepKind.Test, true)]
    public async Task recovery_of_linked_or_orphaned_findings_preserves_opt_out_and_never_duplicates_the_issue(StepKind source, bool linked)
    {
        using var scenario = new WorkflowScenario(101);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(101, "Ticket", parent: 100);
        var failure = new GitHubApiException(GitHubApiErrorKind.Transient, "Finding response lost");
        scenario.Issues.FindingLinkFailure = linked ? null : failure;
        scenario.Issues.FindingResponseFailure = linked ? failure : null;
        string run;
        await using (var first = new WorkflowHost(scenario, new AgentScript { FindingOnFirstCycle = source }))
        {
            var api = new WorkflowApi(first.CreateClient(), scenario.Logs);
            int repository = await api.RegisterRepositoryAsync(scenario);
            run = await api.EnqueueAsync(repository, 100);
            await scenario.Issues.FindingIssuanceFailed.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
            Assert.Equal(["webdevloop"], (await scenario.Issues.GetIssueAsync(scenario.Issues.Ref(102), Ct)).Labels);
        }

        scenario.Issues.SetLabels(102, "ready-for-human");
        scenario.Issues.FindingLinkFailure = null;
        scenario.Issues.FindingResponseFailure = null;
        await using var second = new WorkflowHost(scenario);
        var recoveredApi = new WorkflowApi(second.CreateClient(), scenario.Logs);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        while (!(await recoveredApi.TicketsAsync(run)).Any(ticket => ticket.GetProperty("issueNumber").GetInt32() == 102))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), deadline.Token);
        }

        Assert.Equal(2, (await recoveredApi.TicketsAsync(run)).Length);
        Assert.Equal(["ready-for-human"], (await scenario.Issues.GetIssueAsync(scenario.Issues.Ref(102), Ct)).Labels);
        Assert.Equal([101, 102], (await scenario.Issues.GetSubIssuesAsync(scenario.Issues.Ref(100), Ct)).Select(issue => issue.Ref.Number));
    }
}
