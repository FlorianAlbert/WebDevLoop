using WebDevLoop.Core.Orchestration.Recovery.AgentSteps;
using WebDevLoop.Core.Orchestration.Recovery.ExternalState;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Recovery.Startup;

/// <summary>
/// The single recovery entry point. <see cref="RunStartupAsync"/> runs once per process after prerequisites were evaluated
/// and before any scheduler; <see cref="RunPeriodicAsync"/> runs on a timer afterwards. A cycle runs, in order:
/// <list type="number">
/// <item>external reconciliation — Git refs, worktrees, sagas, PR stacks, issues, finding fingerprints, and merge tracking of
/// awaiting-merge specs, so later stages see the real external state;</item>
/// <item>agent-step recovery — Copilot runtime maintenance, killing orphaned tester processes and releasing their leases,
/// finishing interrupted steps, and relaunching stalled work on the restored worktrees;</item>
/// <item>outbox replay (startup only) — events the previous process persisted but never dispatched;</item>
/// <item>queue recomputation — scheduling of every enabled repository's spec queue;</item>
/// <item>frontier recomputation — durable reconciliation requests for every non-terminal spec;</item>
/// <item>scheduler start (startup only) — opens the <see cref="SchedulerStartGate"/>.</item>
/// </list>
/// Nothing runs while prerequisites are unhealthy (diagnostic-only mode). Each stage isolates its own failures per
/// repository and spec; a stage that throws is reported and the following stages still run, because every stage is
/// idempotent, claims work with compare-and-swap, and the next periodic cycle retries. Run cycles sequentially; each
/// cycle should get a fresh unit-of-work scope.
/// </summary>
public sealed class RecoveryCoordinator(
    IExternalStateRecovery externalState,
    IAgentStepRecovery agentSteps,
    IOutboxReplay outbox,
    ISpecQueueRecomputation specQueues,
    IFrontierReconciliationTrigger frontiers,
    SchedulerStartGate schedulers)
{
    public async Task<RecoveryCycleReport> RunStartupAsync(PrerequisiteReport? prerequisites, CancellationToken cancellationToken)
    {
        if (prerequisites is not { IsReady: true })
        {
            return RecoveryCycleReport.Skipped(RecoveryCycleKind.Startup, RecoveryCycleOutcome.PrerequisitesUnhealthy);
        }

        RecoveryCycleReport report = await RunStagesAsync(RecoveryCycleKind.Startup, cancellationToken);
        schedulers.Open();
        return report with { SchedulersStarted = true };
    }

    public async Task<RecoveryCycleReport> RunPeriodicAsync(PrerequisiteReport? prerequisites, CancellationToken cancellationToken)
    {
        if (prerequisites is not { IsReady: true })
        {
            return RecoveryCycleReport.Skipped(RecoveryCycleKind.Periodic, RecoveryCycleOutcome.PrerequisitesUnhealthy);
        }

        if (!schedulers.IsOpen)
        {
            return RecoveryCycleReport.Skipped(RecoveryCycleKind.Periodic, RecoveryCycleOutcome.StartupPending);
        }

        return await RunStagesAsync(RecoveryCycleKind.Periodic, cancellationToken);
    }

    private async Task<RecoveryCycleReport> RunStagesAsync(RecoveryCycleKind kind, CancellationToken cancellationToken)
    {
        var faults = new List<RecoveryStageFault>();
        ExternalReconciliationReport? external = await RunStageAsync(RecoveryStage.ExternalState, externalState.ReconcileAsync, faults, cancellationToken);
        AgentStepRecoveryReport? agents = await RunStageAsync(RecoveryStage.AgentSteps, agentSteps.RecoverAsync, faults, cancellationToken);
        int? replayed = kind == RecoveryCycleKind.Startup
            ? await RunStageAsync<int?>(RecoveryStage.OutboxReplay, async token => await outbox.ReplayPendingAsync(token), faults, cancellationToken)
            : null;
        QueueRecomputationReport? queues = await RunStageAsync(RecoveryStage.SpecQueues, specQueues.RecomputeAsync, faults, cancellationToken);
        int? frontierRequests = await RunStageAsync<int?>(RecoveryStage.Frontiers, async token => await frontiers.RaiseAsync(token), faults, cancellationToken);
        return new RecoveryCycleReport(kind, RecoveryCycleOutcome.Completed, external, agents, replayed, queues, frontierRequests, faults, SchedulersStarted: false);
    }

    private static async Task<T?> RunStageAsync<T>(
        RecoveryStage stage,
        Func<CancellationToken, Task<T>> run,
        List<RecoveryStageFault> faults,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await run(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            faults.Add(new RecoveryStageFault(stage, exception.Message));
            return default;
        }
    }
}
