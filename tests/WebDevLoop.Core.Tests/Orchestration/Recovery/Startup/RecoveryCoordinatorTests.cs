using WebDevLoop.Core.Orchestration.Recovery.Startup;

namespace WebDevLoop.Core.Tests.Orchestration.Recovery.Startup;

public sealed class RecoveryCoordinatorTests
{
    private readonly RecoveryStageFakes _stages = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task startup_recovers_external_state_then_agent_steps_replays_the_outbox_recomputes_queues_and_frontiers_then_starts_schedulers()
    {
        RecoveryCycleReport report = await _stages.Coordinator().RunStartupAsync(RecoveryStageFakes.Healthy, Token);

        Assert.Equal(["ExternalState", "AgentSteps", "OutboxReplay", "SpecQueues", "schedulers started: False", "Frontiers"], _stages.Journal);
        Assert.True(_stages.Gate.IsOpen);
        Assert.Equal((RecoveryCycleKind.Startup, RecoveryCycleOutcome.Completed, true), (report.Kind, report.Outcome, report.SchedulersStarted));
        Assert.Same(RecoveryStageFakes.EmptyExternalReport, report.ExternalState);
        Assert.Same(RecoveryStageFakes.EmptyAgentReport, report.AgentSteps);
        Assert.Equal((3, 2), (report.ReplayedMessages, report.FrontierRequests));
        Assert.Same(QueueRecomputationReport.Empty, report.SpecQueues);
        Assert.Empty(report.Faults);
    }

    [Fact]
    public async Task diagnostic_only_prerequisites_prevent_recovery_and_scheduler_restart()
    {
        RecoveryCycleReport report = await _stages.Coordinator().RunStartupAsync(RecoveryStageFakes.DiagnosticOnly, Token);

        Assert.Equal(RecoveryCycleOutcome.PrerequisitesUnhealthy, report.Outcome);
        Assert.False(report.SchedulersStarted);
        Assert.False(_stages.Gate.IsOpen);
        Assert.Empty(_stages.Journal);
    }

    [Fact]
    public async Task prerequisites_that_were_never_evaluated_count_as_unhealthy()
    {
        RecoveryCycleReport report = await _stages.Coordinator().RunStartupAsync(prerequisites: null, Token);

        Assert.Equal(RecoveryCycleOutcome.PrerequisitesUnhealthy, report.Outcome);
        Assert.False(_stages.Gate.IsOpen);
        Assert.Empty(_stages.Journal);
    }

    [Fact]
    public async Task startup_after_prerequisites_were_fixed_recovers_and_starts_the_schedulers()
    {
        await _stages.Coordinator().RunStartupAsync(RecoveryStageFakes.DiagnosticOnly, Token);

        RecoveryCycleReport report = await _stages.Coordinator().RunStartupAsync(RecoveryStageFakes.Healthy, Token);

        Assert.Equal(RecoveryCycleOutcome.Completed, report.Outcome);
        Assert.True(_stages.Gate.IsOpen);
    }

    [Fact]
    public async Task periodic_cycle_repeats_the_recurring_stages_without_replaying_the_outbox()
    {
        await _stages.Coordinator().RunStartupAsync(RecoveryStageFakes.Healthy, Token);
        _stages.Journal.Clear();

        RecoveryCycleReport report = await _stages.Coordinator().RunPeriodicAsync(RecoveryStageFakes.Healthy, Token);

        Assert.Equal(["ExternalState", "AgentSteps", "SpecQueues", "schedulers started: True", "Frontiers"], _stages.Journal);
        Assert.Equal((RecoveryCycleKind.Periodic, RecoveryCycleOutcome.Completed, false), (report.Kind, report.Outcome, report.SchedulersStarted));
        Assert.Null(report.ReplayedMessages);
        Assert.Equal(2, report.FrontierRequests);
    }

    [Fact]
    public async Task periodic_cycle_does_nothing_before_startup_recovery_started_the_schedulers()
    {
        RecoveryCycleReport report = await _stages.Coordinator().RunPeriodicAsync(RecoveryStageFakes.Healthy, Token);

        Assert.Equal(RecoveryCycleOutcome.StartupPending, report.Outcome);
        Assert.Empty(_stages.Journal);
        Assert.False(_stages.Gate.IsOpen);
    }

    [Fact]
    public async Task periodic_cycle_is_skipped_while_prerequisites_are_unhealthy()
    {
        await _stages.Coordinator().RunStartupAsync(RecoveryStageFakes.Healthy, Token);
        _stages.Journal.Clear();

        RecoveryCycleReport report = await _stages.Coordinator().RunPeriodicAsync(RecoveryStageFakes.DiagnosticOnly, Token);

        Assert.Equal(RecoveryCycleOutcome.PrerequisitesUnhealthy, report.Outcome);
        Assert.Empty(_stages.Journal);
    }

    [Fact]
    public async Task a_failing_stage_is_reported_and_the_later_stages_still_run_and_start_the_schedulers()
    {
        _stages.Failing.Add(RecoveryStage.ExternalState);
        _stages.Failing.Add(RecoveryStage.OutboxReplay);

        RecoveryCycleReport report = await _stages.Coordinator().RunStartupAsync(RecoveryStageFakes.Healthy, Token);

        Assert.Equal(["ExternalState", "AgentSteps", "OutboxReplay", "SpecQueues", "schedulers started: False", "Frontiers"], _stages.Journal);
        Assert.Equal(
            [new RecoveryStageFault(RecoveryStage.ExternalState, "ExternalState failed."), new RecoveryStageFault(RecoveryStage.OutboxReplay, "OutboxReplay failed.")],
            report.Faults);
        Assert.Null(report.ExternalState);
        Assert.Null(report.ReplayedMessages);
        Assert.NotNull(report.AgentSteps);
        Assert.Equal(RecoveryCycleOutcome.Completed, report.Outcome);
        Assert.True(report.SchedulersStarted);
        Assert.True(_stages.Gate.IsOpen);
    }

    [Fact]
    public async Task cancellation_stops_the_cycle_without_starting_the_schedulers()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _stages.Coordinator().RunStartupAsync(RecoveryStageFakes.Healthy, cancellation.Token));

        Assert.False(_stages.Gate.IsOpen);
    }

    [Fact]
    public async Task the_scheduler_gate_releases_waiting_schedulers_once_opened()
    {
        Task waiting = _stages.Gate.WaitUntilOpenAsync(Token);
        bool completedBeforeStartup = waiting.IsCompleted;

        await _stages.Coordinator().RunStartupAsync(RecoveryStageFakes.Healthy, Token);
        await waiting.WaitAsync(TimeSpan.FromSeconds(5), Token);

        Assert.False(completedBeforeStartup);
        Assert.True(waiting.IsCompletedSuccessfully);
    }
}
