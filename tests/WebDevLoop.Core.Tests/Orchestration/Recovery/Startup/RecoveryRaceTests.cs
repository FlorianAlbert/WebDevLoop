using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Recovery.Startup;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Tests.Orchestration.Recovery.AgentSteps;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.Recovery.Startup;

/// <summary>
/// Recovery cycles with the real agent-step recovery, frontier signal, and frontier event handler over the compare-and-swap
/// workflow database: the outbox replay and the "schedulers" deliver committed events to the frontier handler.
/// </summary>
public sealed class RecoveryRaceTests
{
    private readonly AgentStepRecoveryFixture _fixture = new();
    private readonly SchedulerStartGate _gate = new();

    private TicketExecutionFixture Execution => _fixture.Execution;

    private static CancellationToken Token => AgentStepRecoveryFixture.Token;

    [Fact]
    public async Task duplicate_recovery_and_normal_frontier_callback_start_one_implementer_session()
    {
        SeededSpec spec = await Execution.SeedRunningSpecAsync("app", (1, []));
        Execution.UseRunner();
        await Execution.ReconcileAsync(spec.Id);
        StepRun crashed = Assert.Single(Execution.Steps(spec[1]));
        int launchedBeforeRestart = Execution.Launcher.Launched.Count;
        _fixture.Restart();

        await Coordinator().RunStartupAsync(RecoveryStageFakes.Healthy, Token);
        await Execution.HandleAsync(new SpecRunStatusChanged(spec.Id, spec.RepositoryId, SpecRunStatus.Preparing, SpecRunStatus.Running, Execution.Clock.UtcNow));
        await Coordinator().RunPeriodicAsync(RecoveryStageFakes.Healthy, Token);
        await Execution.PumpEventsAsync();

        Assert.Single(Execution.Launcher.Launched.Skip(launchedBeforeRestart));
        AgentRunRequestAssert.SingleResumeOf(Execution.Agents, crashed);
        Assert.Single(Execution.Agents.Started);
        StepRun[] steps = Execution.Steps(spec[1]).ToArray();
        Assert.Equal([crashed.Id], steps.Where(step => !step.IsActive).Select(step => step.Id));
        Assert.Single(steps, step => step.IsActive);
        Assert.Equal((TicketRunStatus.Implementing, 1), (Execution.Ticket(spec[1]).Status, Execution.Ticket(spec[1]).Attempt));
    }

    [Fact]
    public async Task outbox_replay_plus_reconciliation_does_not_duplicate_events_or_steps()
    {
        SeededSpec spec = await Execution.SeedRunningSpecAsync("app", (1, []), (2, [1]));
        await Execution.ReconcileAsync(spec.Id);
        Execution.Db.TakeUndispatchedEvents();
        // The previous process integrated ticket #1 and died before dispatching the resulting events.
        await Execution.IntegrateAsync(spec, spec[1]);
        _fixture.Restart();
        Execution.UseRunner();

        RecoveryCycleReport startup = await Coordinator().RunStartupAsync(RecoveryStageFakes.Healthy, Token);
        await Execution.PumpEventsAsync();
        await Coordinator().RunPeriodicAsync(RecoveryStageFakes.Healthy, Token);
        await Execution.PumpEventsAsync();

        Assert.Equal(3, startup.ReplayedMessages);
        Assert.Equal([new ImplementationAssignment(spec.Id, spec[2])], Execution.Launcher.Launched.Skip(1));
        Assert.Single(Execution.Steps(spec[2]));
        Assert.Equal(1, Execution.Ticket(spec[2]).Attempt);
        TicketRunStatusChanged[] ticket2 = Execution.Db.CommittedEvents.OfType<TicketRunStatusChanged>().Where(changed => changed.TicketRunId == spec[2]).ToArray();
        Assert.Equal(
            [(TicketRunStatus.Blocked, TicketRunStatus.Ready), (TicketRunStatus.Ready, TicketRunStatus.Implementing)],
            ticket2.Select(changed => (changed.From, changed.To)));
        Assert.Equal(2, Execution.Db.CommittedEvents.OfType<FrontierReconciliationRequested>().Count());
    }

    /// <summary>One coordinator per cycle, like one DI scope per cycle.</summary>
    private RecoveryCoordinator Coordinator()
    {
        CasWorkflowScope scope = Execution.Db.OpenScope();
        var noStages = new RecoveryStageFakes();
        return new RecoveryCoordinator(
            noStages,
            _fixture.Service(),
            new FrontierHandlerReplay(Execution),
            noStages,
            new FrontierReconciliationSignal(scope, scope, scope, Execution.Clock),
            _gate);
    }

    /// <summary>Outbox replay: delivers every committed, undispatched event to the frontier handler.</summary>
    private sealed class FrontierHandlerReplay(TicketExecutionFixture execution) : IOutboxReplay
    {
        public async Task<int> ReplayPendingAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<WorkflowEvent> pending = execution.Db.TakeUndispatchedEvents();
            foreach (WorkflowEvent workflowEvent in pending)
            {
                await execution.HandleAsync(workflowEvent);
            }

            return pending.Count;
        }
    }
}

internal static class AgentRunRequestAssert
{
    public static void SingleResumeOf(ImplementerAgentStub agents, StepRun interrupted) =>
        Assert.Equal(interrupted.CopilotSessionId, Assert.Single(agents.Resumed).SessionId.Value);
}
