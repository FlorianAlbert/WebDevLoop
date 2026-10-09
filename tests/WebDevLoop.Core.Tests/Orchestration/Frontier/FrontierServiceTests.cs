using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Frontier;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.Frontier;

public sealed class FrontierServiceTests
{
    private readonly TicketExecutionFixture _fixture = new();

    [Fact]
    public async Task reconciling_a_running_spec_promotes_unblocked_tickets_and_starts_them()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []), (2, [1]));

        FrontierResult result = await _fixture.ReconcileAsync(spec.Id);

        Assert.Equal(FrontierOutcome.Reconciled, result.Outcome);
        Assert.Equal([spec[1]], result.Unblocked);
        Assert.Equal([spec[1]], result.Dispatched);
        Assert.Equal(TicketRunStatus.Implementing, _fixture.Ticket(spec[1]).Status);
        Assert.Equal(TicketRunStatus.Blocked, _fixture.Ticket(spec[2]).Status);
        Assert.Equal([new ImplementationAssignment(spec.Id, spec[1])], _fixture.Launcher.Launched);
        Assert.Equal(
            [(TicketRunStatus.Blocked, TicketRunStatus.Ready), (TicketRunStatus.Ready, TicketRunStatus.Implementing)],
            _fixture.Db.CommittedEvents.OfType<TicketRunStatusChanged>().Select(changed => (changed.From, changed.To)));
    }

    [Fact]
    public async Task diamond_dag_starts_both_middle_tickets_after_root_integrates_while_others_still_run()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []), (2, [1]), (3, [1]), (4, [2, 3]), (5, []));
        await _fixture.HandleAsync(new SpecRunStatusChanged(spec.Id, spec.RepositoryId, SpecRunStatus.Preparing, SpecRunStatus.Running, TicketExecutionFixture.T0));
        await _fixture.PumpEventsAsync();
        Assert.Equal([spec[1], spec[5]], _fixture.Launcher.Launched.Select(assignment => assignment.TicketRunId));

        await _fixture.IntegrateAsync(spec, spec[1]);
        await _fixture.PumpEventsAsync();

        Assert.Equal([spec[1], spec[5], spec[2], spec[3]], _fixture.Launcher.Launched.Select(assignment => assignment.TicketRunId));
        Assert.Equal(TicketRunStatus.Implementing, _fixture.Ticket(spec[2]).Status);
        Assert.Equal(TicketRunStatus.Implementing, _fixture.Ticket(spec[3]).Status);
        Assert.Equal(TicketRunStatus.Implementing, _fixture.Ticket(spec[5]).Status);
        Assert.Equal(TicketRunStatus.Blocked, _fixture.Ticket(spec[4]).Status);
    }

    [Fact]
    public async Task bottom_of_the_diamond_starts_only_once_both_middle_tickets_are_integrated()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []), (2, [1]), (3, [1]), (4, [2, 3]));
        await _fixture.ReconcileAsync(spec.Id);
        await _fixture.IntegrateAsync(spec, spec[1]);
        await _fixture.PumpEventsAsync();

        await _fixture.IntegrateAsync(spec, spec[2]);
        await _fixture.PumpEventsAsync();
        TicketRunStatus whileOneMiddleRuns = _fixture.Ticket(spec[4]).Status;
        await _fixture.IntegrateAsync(spec, spec[3]);
        await _fixture.PumpEventsAsync();

        Assert.Equal(TicketRunStatus.Blocked, whileOneMiddleRuns);
        Assert.Equal(TicketRunStatus.Implementing, _fixture.Ticket(spec[4]).Status);
        Assert.Single(_fixture.Launcher.Launched, assignment => assignment.TicketRunId == spec[4]);
    }

    [Fact]
    public async Task duplicate_frontier_events_launch_exactly_one_implementer()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []));
        var request = new FrontierReconciliationRequested(spec.Id, TicketExecutionFixture.T0);

        await _fixture.HandleAsync(request);
        await _fixture.HandleAsync(request);
        await _fixture.HandleAsync(new SpecRunStatusChanged(spec.Id, spec.RepositoryId, SpecRunStatus.Preparing, SpecRunStatus.Running, TicketExecutionFixture.T0));
        await _fixture.PumpEventsAsync();

        Assert.Single(_fixture.Launcher.Launched);
        Assert.Single(_fixture.Db.CommittedEvents.OfType<TicketRunStatusChanged>(), changed => changed.To == TicketRunStatus.Implementing);
    }

    [Fact]
    public async Task a_spec_that_is_not_running_is_left_alone()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []));
        CasWorkflowScope scope = _fixture.Db.OpenScope();
        (await scope.GetAsync(spec.Id, TicketExecutionFixture.Token))!.TransitionTo(SpecRunStatus.ParentReviewing, TicketExecutionFixture.T0);
        await scope.SaveChangesAsync(TicketExecutionFixture.Token);

        FrontierResult result = await _fixture.ReconcileAsync(spec.Id);

        Assert.Equal(FrontierOutcome.NotRunning, result.Outcome);
        Assert.Equal(TicketRunStatus.Blocked, _fixture.Ticket(spec[1]).Status);
        Assert.Empty(_fixture.Launcher.Launched);
    }

    [Fact]
    public async Task a_retried_ticket_is_dispatched_again_when_it_returns_to_ready()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []));
        await _fixture.ReconcileAsync(spec.Id);
        await _fixture.MoveAsync(spec[1], TicketRunStatus.NeedsAttention);
        _fixture.Db.TakeUndispatchedEvents();

        await _fixture.MoveAsync(spec[1], TicketRunStatus.Ready);
        await _fixture.PumpEventsAsync();

        Assert.Equal(2, _fixture.Launcher.Launched.Count);
        Assert.Equal(TicketRunStatus.Implementing, _fixture.Ticket(spec[1]).Status);
        Assert.Equal(2, _fixture.Ticket(spec[1]).Attempt);
    }
}
