using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;
using WebDevLoop.Core.Orchestration.Recovery.AgentSteps;
using WebDevLoop.Core.Orchestration.Recovery.ExternalState;
using WebDevLoop.Core.Orchestration.Recovery.Startup;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Orchestration.Recovery.Startup;

/// <summary>Recovery stages that journal their calls and can be told to fail, plus helpers to build a coordinator.</summary>
internal sealed class RecoveryStageFakes : IExternalStateRecovery, IAgentStepRecovery, IOutboxReplay, ISpecQueueRecomputation, IFrontierReconciliationTrigger
{
    public static readonly PrerequisiteReport Healthy = new(
    [
        new PrerequisiteCheck("sqlite", PrerequisiteStatus.Passed, "Migrations applied."),
        new PrerequisiteCheck("gh stack", PrerequisiteStatus.Warning, "Optional gh stack extension missing."),
    ]);

    public static readonly PrerequisiteReport DiagnosticOnly = new(
    [
        new PrerequisiteCheck("sqlite", PrerequisiteStatus.Passed, "Migrations applied."),
        new PrerequisiteCheck("playwright-cli", PrerequisiteStatus.Failed, "playwright-cli is not installed."),
    ]);

    public SchedulerStartGate Gate { get; } = new();

    /// <summary>Stage calls in order; the frontier stage also records whether the schedulers had started.</summary>
    public List<string> Journal { get; } = [];

    public HashSet<RecoveryStage> Failing { get; } = [];

    public static ExternalReconciliationReport EmptyExternalReport { get; } =
        new([], [], new MergeTrackingPass(new Dictionary<RunId, CompletionResult>(), new Dictionary<RunId, MergeTrackingResult>()));

    public static AgentStepRecoveryReport EmptyAgentReport { get; } = new([], [], null, [], [], [], [], [], [], 0);

    public RecoveryCoordinator Coordinator() => new(this, this, this, this, this, Gate);

    public Task<ExternalReconciliationReport> ReconcileAsync(CancellationToken cancellationToken) =>
        Run(RecoveryStage.ExternalState, EmptyExternalReport);

    public Task<AgentStepRecoveryReport> RecoverAsync(CancellationToken cancellationToken) => Run(RecoveryStage.AgentSteps, EmptyAgentReport);

    public Task<int> ReplayPendingAsync(CancellationToken cancellationToken) => Run(RecoveryStage.OutboxReplay, 3);

    public Task<QueueRecomputationReport> RecomputeAsync(CancellationToken cancellationToken) =>
        Run(RecoveryStage.SpecQueues, QueueRecomputationReport.Empty);

    public Task<int> RaiseAsync(CancellationToken cancellationToken)
    {
        Journal.Add($"schedulers started: {Gate.IsOpen}");
        return Run(RecoveryStage.Frontiers, 2);
    }

    private Task<T> Run<T>(RecoveryStage stage, T result)
    {
        Journal.Add(stage.ToString());
        return Failing.Contains(stage) ? throw new InvalidOperationException($"{stage} failed.") : Task.FromResult(result);
    }
}
