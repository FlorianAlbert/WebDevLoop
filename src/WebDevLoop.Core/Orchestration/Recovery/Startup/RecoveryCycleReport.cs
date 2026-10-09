using WebDevLoop.Core.Orchestration.Recovery.AgentSteps;
using WebDevLoop.Core.Orchestration.Recovery.ExternalState;

namespace WebDevLoop.Core.Orchestration.Recovery.Startup;

public enum RecoveryCycleKind
{
    /// <summary>Once per process, after prerequisites were evaluated and before the schedulers start.</summary>
    Startup,

    /// <summary>On a timer while the schedulers run, so a lost event or a crashed runner cannot stall work.</summary>
    Periodic,
}

public enum RecoveryCycleOutcome
{
    Completed,

    /// <summary>Prerequisites failed (or were not evaluated): diagnostic-only mode, nothing ran and no scheduler started.</summary>
    PrerequisitesUnhealthy,

    /// <summary>A periodic cycle before startup recovery completed; nothing ran.</summary>
    StartupPending,
}

/// <summary>Recovery stages in the order a cycle runs them.</summary>
public enum RecoveryStage
{
    ExternalState,
    AgentSteps,
    OutboxReplay,
    SpecQueues,
    Frontiers,
}

public sealed record RecoveryStageFault(RecoveryStage Stage, string Error);

/// <summary>What one recovery cycle did. Stage results are null when the stage did not run or failed.</summary>
/// <param name="Faults">Stages that threw; the following stages still ran.</param>
/// <param name="SchedulersStarted">This cycle opened the <see cref="SchedulerStartGate"/>.</param>
public sealed record RecoveryCycleReport(
    RecoveryCycleKind Kind,
    RecoveryCycleOutcome Outcome,
    ExternalReconciliationReport? ExternalState,
    AgentStepRecoveryReport? AgentSteps,
    int? ReplayedMessages,
    QueueRecomputationReport? SpecQueues,
    int? FrontierRequests,
    IReadOnlyList<RecoveryStageFault> Faults,
    bool SchedulersStarted)
{
    public static RecoveryCycleReport Skipped(RecoveryCycleKind kind, RecoveryCycleOutcome outcome) =>
        new(kind, outcome, null, null, null, null, null, [], SchedulersStarted: false);
}
