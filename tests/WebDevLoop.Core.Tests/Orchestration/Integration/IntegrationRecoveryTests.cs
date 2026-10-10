using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Integration;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Orchestration.Integration;

public sealed class IntegrationRecoveryTests
{
    /// <summary>Saga start plus one save per checkpoint from <c>SquashCommitCreated</c> to <c>Completed</c>.</summary>
    private const int SavesPerSaga = 10;

    /// <summary>squash, integration ref, integration push, stack push, PR, stack link, issue close.</summary>
    private const int SideEffectsOfUpperLayer = 7;

    public static TheoryData<int> EverySave => [.. Enumerable.Range(1, SavesPerSaga)];

    public static TheoryData<int> EverySideEffect => [.. Enumerable.Range(1, SideEffectsOfUpperLayer)];

    [Theory]
    [MemberData(nameof(EverySave))]
    public async Task Crash_after_each_checkpoint_resumes_without_duplicate_external_calls(int crashAfterSave)
    {
        string[] uninterrupted = await UpperLayerCallsAsync(new IntegrationFixture());
        var f = new IntegrationFixture();
        (SpecRun _, TicketRun ticket, int before) = await SeedUpperLayerAsync(f);

        await Assert.ThrowsAsync<SimulatedCrashException>(() => f.IntegrateAsync(ticket, new CrashingUnitOfWork(f.Store, f.Journal, crashAfterSave)));
        f.Journal.Restart();
        await f.IntegrateAsync(ticket);

        Assert.Equal(uninterrupted, f.Journal.Calls.Skip(before));
        Assert.Equal(TicketRunStatus.Integrated, ticket.Status);
        Assert.Single(f.Transitions(ticket, TicketRunStatus.Integrated));
    }

    [Theory]
    [MemberData(nameof(EverySideEffect))]
    public async Task Crash_right_after_each_external_side_effect_resumes_without_duplicating_commits_pull_requests_or_issue_transitions(int crashAfterCall)
    {
        var f = new IntegrationFixture();
        (SpecRun spec, TicketRun ticket, int before) = await SeedUpperLayerAsync(f);
        f.Journal.CrashAfterCall = before + crashAfterCall;

        await Assert.ThrowsAsync<SimulatedCrashException>(() => f.IntegrateAsync(ticket));
        f.Journal.Restart();
        IntegrationResult resumed = await f.IntegrateAsync(ticket);

        Assert.Equal(IntegrationOutcome.Integrated, resumed.Outcome);
        IReadOnlyList<PullStackLayer> layers = f.Layers(spec);
        Assert.Equal(2, layers.Count);
        Assert.Equal([layers[0].CommitSha], f.Git.ParentsOf(layers[1].CommitSha));
        Assert.Equal(layers[1].CommitSha, f.LocalTip(spec.IntegrationBranch));
        Assert.Equal(layers[1].CommitSha, f.Git.RemoteTip(spec.IntegrationBranch));
        Assert.Equal(2, f.Pulls.PullRequests.Count);
        PullStackSnapshot stack = Assert.Single(f.Pulls.Stacks);
        Assert.Equal(layers.Select(layer => layer.PullRequestNumber), stack.BottomToTop);
        Assert.Single(f.CallsOf($"create-pr:{layers[1].BranchName}"));
        Assert.Equal(IssueState.Closed, f.IssueState(ticket));
        Assert.Single(f.Transitions(ticket, TicketRunStatus.Integrated));
        Assert.True(f.Saga(ticket)!.IsCompleted);
    }

    [Fact]
    public async Task Transient_github_failure_is_recorded_and_the_next_run_completes_the_saga_once()
    {
        var f = new IntegrationFixture();
        SpecRun spec = f.SeedRunningSpec();
        TicketRun ticket = f.SeedReviewedTicket(spec, 1, "feature.cs");
        f.JournaledPulls.FailNextCreate = new HttpRequestException("503 Service Unavailable");

        IntegrationResult failed = await f.IntegrateAsync(ticket);

        Assert.Equal(IntegrationOutcome.Faulted, failed.Outcome);
        Assert.Equal(TicketRunStatus.Integrating, ticket.Status);
        IntegrationSaga saga = f.Saga(ticket)!;
        Assert.Equal(IntegrationSagaCheckpoint.StackBranchPushed, saga.Checkpoint);
        Assert.Contains("503", saga.LastError, StringComparison.Ordinal);

        IntegrationResult retried = await f.IntegrateAsync(ticket);

        Assert.Equal(IntegrationOutcome.Integrated, retried.Outcome);
        Assert.Single(f.Pulls.PullRequests);
        Assert.Single(f.CallsOf("squash"));
        Assert.Null(saga.LastError);
    }

    [Fact]
    public async Task Later_ticket_first_finishes_an_earlier_ticket_whose_layer_was_interrupted_after_moving_the_integration_branch()
    {
        var f = new IntegrationFixture();
        SpecRun spec = f.SeedRunningSpec();
        TicketRun first = f.SeedReviewedTicket(spec, 1, "first.cs");
        TicketRun second = f.SeedReviewedTicket(spec, 2, "second.cs");
        const int IntegrationRefUpdatedSave = 3;
        await Assert.ThrowsAsync<SimulatedCrashException>(() => f.IntegrateAsync(first, new CrashingUnitOfWork(f.Store, f.Journal, IntegrationRefUpdatedSave)));
        f.Journal.Restart();
        Assert.Equal(IntegrationSagaCheckpoint.IntegrationRefUpdated, f.Saga(first)!.Checkpoint);

        IntegrationResult result = await f.IntegrateAsync(second);

        Assert.Equal(IntegrationOutcome.Integrated, result.Outcome);
        Assert.Equal(TicketRunStatus.Integrated, first.Status);
        IReadOnlyList<PullStackLayer> layers = f.Layers(spec);
        Assert.Equal([first.Id, second.Id], layers.Select(layer => layer.TicketRunId));
        Assert.Equal([layers[0].CommitSha], f.Git.ParentsOf(layers[1].CommitSha));
    }

    [Fact]
    public async Task Later_ticket_waits_while_an_earlier_layer_that_moved_the_integration_branch_needs_attention()
    {
        var f = new IntegrationFixture();
        SpecRun spec = f.SeedRunningSpec();
        TicketRun first = f.SeedReviewedTicket(spec, 1, "first.cs");
        TicketRun second = f.SeedReviewedTicket(spec, 2, "second.cs");
        f.JournaledPulls.FailNextCreate = new HttpRequestException("503 Service Unavailable");
        await f.IntegrateAsync(first);
        first.MarkNeedsAttention(AttentionReasons.Unclassified("Operator paused the ticket.", true), IntegrationFixture.T0);
        CommitSha tip = f.LocalTip(spec.IntegrationBranch);

        IntegrationResult result = await f.IntegrateAsync(second);

        Assert.Equal(IntegrationOutcome.WaitingForEarlierLayer, result.Outcome);
        Assert.Contains(first.Id.Value, result.Reason, StringComparison.Ordinal);
        Assert.Equal(TicketRunStatus.Integrating, second.Status);
        Assert.Null(f.Saga(second));
        Assert.Equal(tip, f.LocalTip(spec.IntegrationBranch));
        Assert.Single(f.CallsOf("squash"));
    }

    [Fact]
    public async Task Saga_that_stopped_before_moving_the_integration_branch_squashes_again_onto_the_current_tip()
    {
        var f = new IntegrationFixture();
        SpecRun spec = f.SeedRunningSpec();
        TicketRun first = f.SeedReviewedTicket(spec, 1, "first.cs");
        TicketRun second = f.SeedReviewedTicket(spec, 2, "second.cs");
        f.Git.ConflictOnNextSquash = ["shared.cs"];
        f.Agents.Script(AgentRole.ConflictResolver, _ => new ConflictResolutionReport(ConflictResolutionStatus.Blocked, null, [], "Needs a product decision.", []));
        Assert.Equal(IntegrationOutcome.NeedsAttention, (await f.IntegrateAsync(first)).Outcome);
        Assert.Equal(IntegrationOutcome.Integrated, (await f.IntegrateAsync(second)).Outcome);
        CommitSha secondLayer = f.LocalTip(spec.IntegrationBranch);
        foreach (TicketRunStatus next in new[] { TicketRunStatus.Ready, TicketRunStatus.Implementing, TicketRunStatus.Reviewing })
        {
            first.TransitionTo(next, IntegrationFixture.T0);
        }

        IntegrationResult retried = await f.IntegrateAsync(first);

        Assert.Equal(IntegrationOutcome.Integrated, retried.Outcome);
        IntegrationSaga saga = f.Saga(first)!;
        Assert.Equal(secondLayer, saga.ExpectedPriorIntegrationSha);
        Assert.Equal([secondLayer], f.Git.ParentsOf(saga.SquashCommitSha!.Value));
        Assert.Equal([second.Id, first.Id], f.Layers(spec).Select(layer => layer.TicketRunId));
        Assert.Equal(2, f.Store.PendingEvents.OfType<SagaCheckpointAdvanced>().Count(announced => announced.TicketRunId == first.Id && announced.Checkpoint == IntegrationSagaCheckpoint.Started));
    }

    private static async Task<string[]> UpperLayerCallsAsync(IntegrationFixture f)
    {
        (_, TicketRun ticket, int before) = await SeedUpperLayerAsync(f);
        Assert.Equal(IntegrationOutcome.Integrated, (await f.IntegrateAsync(ticket)).Outcome);
        string[] calls = f.Journal.Calls.Skip(before).ToArray();
        Assert.Equal(SideEffectsOfUpperLayer, calls.Length);
        return calls;
    }

    /// <summary>A spec whose bottom layer is integrated, and a reviewed ticket that will become the second layer.</summary>
    private static async Task<(SpecRun Spec, TicketRun Ticket, int CallsBefore)> SeedUpperLayerAsync(IntegrationFixture f)
    {
        SpecRun spec = f.SeedRunningSpec();
        TicketRun bottom = f.SeedReviewedTicket(spec, 1, "bottom.cs");
        TicketRun ticket = f.SeedReviewedTicket(spec, 2, "upper.cs");
        Assert.Equal(IntegrationOutcome.Integrated, (await f.IntegrateAsync(bottom)).Outcome);
        return (spec, ticket, f.Journal.Calls.Count);
    }
}
