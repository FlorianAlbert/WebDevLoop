using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Frontier;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Issues;

namespace WebDevLoop.Web.Tests.Workflow;

public sealed class TicketBlockerWorkflowTests
{
    [Fact]
    public async Task an_open_unlabeled_blocker_outside_the_spec_prevents_implementation_until_closed()
    {
        using var scenario = new WorkflowScenario(101);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(900, "External blocker");
        scenario.Issues.SetLabels(900);
        scenario.Issues.Seed(101, "Ticket", parent: 100, blockedBy: 900);
        var script = new AgentScript();
        script.HangingImplementers.Add(101);
        await using var host = new WorkflowHost(scenario, script);
        var api = new WorkflowApi(host.CreateClient(), scenario.Logs);
        int repository = await api.RegisterRepositoryAsync(scenario);
        string run = await api.EnqueueAsync(repository, 100);
        await api.WaitForStatusAsync(run, nameof(SpecRunStatus.Running));

        await ReconcileAsync(host, run);

        Assert.Equal(nameof(TicketRunStatus.Blocked), Assert.Single(await api.TicketsAsync(run)).GetProperty("status").GetString());
        Assert.DoesNotContain(scenario.Journal.Calls, call => call.Role == AgentRole.Implementer);

        await scenario.Issues.CloseAsync(scenario.Issues.Ref(900), IssueCloseReason.Completed, TestContext.Current.CancellationToken);
        await script.HangingImplementerStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(nameof(TicketRunStatus.Implementing), Assert.Single(await api.TicketsAsync(run)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task changed_cross_repository_relationships_are_binding_even_when_an_old_blocker_closed()
    {
        using var scenario = new WorkflowScenario(101);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(900, "Old blocker");
        scenario.Issues.Seed(101, "Ticket", parent: 100, blockedBy: 900);
        var crossRepository = new IssueRef("another-owner", "library", 900);
        scenario.Issues.Seed(crossRepository, IssueState.Open);
        var script = new AgentScript();
        script.HangingImplementers.Add(101);
        await using var host = new WorkflowHost(scenario, script);
        var api = new WorkflowApi(host.CreateClient(), scenario.Logs);
        int repository = await api.RegisterRepositoryAsync(scenario);
        string run = await api.EnqueueAsync(repository, 100);
        await api.WaitForStatusAsync(run, nameof(SpecRunStatus.Running));
        await ReconcileAsync(host, run);

        scenario.Issues.SetBlockedBy(101, scenario.Issues.Ref(900), crossRepository);
        await scenario.Issues.CloseAsync(scenario.Issues.Ref(900), IssueCloseReason.Completed, TestContext.Current.CancellationToken);
        await ReconcileAsync(host, run);

        Assert.Equal(nameof(TicketRunStatus.Blocked), Assert.Single(await api.TicketsAsync(run)).GetProperty("status").GetString());
        Assert.DoesNotContain(scenario.Journal.Calls, call => call.Role == AgentRole.Implementer);

        await scenario.Issues.CloseAsync(crossRepository, IssueCloseReason.Completed, TestContext.Current.CancellationToken);
        await script.HangingImplementerStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Empty(scenario.Pulls.All());
    }

    [Theory]
    [InlineData(true, GitHubApiErrorKind.NotFound)]
    [InlineData(false, GitHubApiErrorKind.NotFound)]
    [InlineData(false, GitHubApiErrorKind.Unauthorized)]
    [InlineData(false, GitHubApiErrorKind.Forbidden)]
    [InlineData(false, GitHubApiErrorKind.Transient)]
    public async Task unavailable_ticket_or_blocker_defers_dispatch_reports_the_error_and_recovers(bool ticketUnavailable, GitHubApiErrorKind kind)
    {
        using var scenario = new WorkflowScenario(101);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(101, "Ticket", parent: 100);
        var blocker = new IssueRef("another-owner", "library", 101);
        scenario.Issues.Seed(blocker, IssueState.Closed);
        scenario.Issues.SetBlockedBy(101, blocker);
        IssueRef unavailable = ticketUnavailable ? scenario.Issues.Ref(101) : blocker;
        const string failure = "Current blocker information is unavailable; retry the frontier.";
        scenario.Issues.FailReads(unavailable, new GitHubApiException(kind, failure));
        await using var host = new WorkflowHost(scenario);
        var api = new WorkflowApi(host.CreateClient(), scenario.Logs);
        int repository = await api.RegisterRepositoryAsync(scenario);
        string run = await api.EnqueueAsync(repository, 100);
        await api.WaitForStatusAsync(run, nameof(SpecRunStatus.Running));

        FrontierResult deferred = await ReconcileAsync(host, run);
        Assert.Equal(FrontierOutcome.GitHubUnavailable, deferred.Outcome);
        Assert.Contains(deferred.LookupErrors, error => error.Message.Contains(failure, StringComparison.Ordinal));
        await host.Services.GetRequiredService<IRunEventBus>().PublishAsync(
            new EventEnvelope(0, new FrontierReconciliationRequested(new RunId(run), DateTimeOffset.UtcNow)),
            TestContext.Current.CancellationToken);
        Assert.Equal(nameof(TicketRunStatus.Blocked), Assert.Single(await api.TicketsAsync(run)).GetProperty("status").GetString());
        Assert.DoesNotContain(scenario.Journal.Calls, call => call.Role == AgentRole.Implementer);
        Assert.Contains(scenario.Logs.Entries, entry => entry.Contains(failure, StringComparison.Ordinal));

        scenario.Issues.FailReads(unavailable, null);
        await api.WaitForStatusAsync(run, nameof(SpecRunStatus.AwaitingMerge));

        Assert.Single(await api.StackAsync(run));
        Assert.Equal(IssueState.Closed, scenario.Issues.StateOf(101));
    }

    [Fact]
    public async Task an_unavailable_blocker_does_not_prevent_an_independent_ticket_from_integrating()
    {
        using var scenario = new WorkflowScenario(101, 102);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(101, "Blocked ticket", parent: 100);
        scenario.Issues.Seed(102, "Independent ticket", parent: 100);
        var blocker = new IssueRef("another-owner", "library", 900);
        scenario.Issues.Seed(blocker, IssueState.Closed);
        scenario.Issues.SetBlockedBy(101, blocker);
        const string failure = "Blocker lookup unavailable for one ticket.";
        scenario.Issues.FailReads(blocker, new GitHubApiException(GitHubApiErrorKind.Transient, failure));
        await using var host = new WorkflowHost(scenario);
        var api = new WorkflowApi(host.CreateClient(), scenario.Logs);
        int repository = await api.RegisterRepositoryAsync(scenario);
        string run = await api.EnqueueAsync(repository, 100);

        await api.WaitForStatusAsync(run, nameof(SpecRunStatus.Running));
        await api.WaitForTicketStatusAsync(run, 102, nameof(TicketRunStatus.Integrated));
        await host.Services.GetRequiredService<IRunEventBus>().PublishAsync(
            new EventEnvelope(0, new FrontierReconciliationRequested(new RunId(run), DateTimeOffset.UtcNow)),
            TestContext.Current.CancellationToken);

        await api.WaitForTicketStatusAsync(run, 101, nameof(TicketRunStatus.Blocked));
        Assert.DoesNotContain(scenario.Journal.Calls, call => call.Role == AgentRole.Implementer && call.TicketIssue == 101);
        Assert.Contains(scenario.Logs.Entries, entry => entry.Contains(failure, StringComparison.Ordinal));

        scenario.Issues.FailReads(blocker, null);
        await api.WaitForStatusAsync(run, nameof(SpecRunStatus.AwaitingMerge));
        Assert.Equal(2, (await api.StackAsync(run)).Length);
    }

    private static async Task<FrontierResult> ReconcileAsync(WorkflowHost host, string run)
    {
        using IServiceScope scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FrontierService>()
            .ReconcileAsync(new RunId(run), TestContext.Current.CancellationToken);
    }
}
