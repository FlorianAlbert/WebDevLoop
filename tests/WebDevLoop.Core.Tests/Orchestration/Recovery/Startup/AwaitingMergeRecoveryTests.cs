using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;
using WebDevLoop.Core.Orchestration.Integration;
using WebDevLoop.Core.Orchestration.Recovery.ExternalState;
using WebDevLoop.Core.Orchestration.Recovery.Startup;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Settings;
using WebDevLoop.Core.Tests.Orchestration.Completion.ReadyAndMerge;
using WebDevLoop.Core.Tests.Orchestration.Recovery.AgentSteps;
using WebDevLoop.Core.Tests.Ports.Fakes;

namespace WebDevLoop.Core.Tests.Orchestration.Recovery.Startup;

/// <summary>Recovery cycles with the real external-state reconciler (incl. merge tracking), queue recomputation, and frontier signal.</summary>
public sealed class AwaitingMergeRecoveryTests
{
    private const int Blocking = 10;
    private const int Dependent = 20;

    private readonly ReadyAndMergeFixture _fixture = new();
    private readonly SchedulerStartGate _gate = new();

    private static CancellationToken Token => ReadyAndMergeFixture.Token;

    [Fact]
    public async Task awaiting_merge_spec_is_polled_and_keeps_its_dependency_lane_occupied_until_completion()
    {
        _fixture.SeedSpec(Blocking);
        _fixture.SeedSpec(Dependent, blockedBySpecs: Blocking);
        SpecRun blocking = await _fixture.AwaitingMergeSpecAsync(Blocking, 11, 12);
        SpecRun dependent = await _fixture.EnqueueAsync(Dependent, 21);

        RecoveryCycleReport startup = await Coordinator().RunStartupAsync(RecoveryStageFakes.Healthy, Token);
        RecoveryCycleReport stillOpen = await Coordinator().RunPeriodicAsync(RecoveryStageFakes.Healthy, Token);
        (SpecRunStatus Blocking, SpecRunStatus Dependent) whileOpen = (blocking.Status, dependent.Status);
        await _fixture.MergeStackAsync(blocking);
        RecoveryCycleReport merged = await Coordinator().RunPeriodicAsync(RecoveryStageFakes.Healthy, Token);

        Assert.Equal(MergeTrackingOutcome.Open, startup.ExternalState!.MergeTracking.Merges[blocking.Id].Outcome);
        Assert.Equal(MergeTrackingOutcome.Open, stillOpen.ExternalState!.MergeTracking.Merges[blocking.Id].Outcome);
        Assert.Equal([dependent.Id], Assert.Single(startup.SpecQueues!.Repositories).Schedule.Waiting);
        Assert.Equal((SpecRunStatus.AwaitingMerge, SpecRunStatus.WaitingForDependency), whileOpen);
        Assert.Equal(MergeTrackingOutcome.Completed, merged.ExternalState!.MergeTracking.Merges[blocking.Id].Outcome);
        Assert.Equal(SpecRunStatus.Completed, blocking.Status);
        Assert.Equal([dependent.Id], Assert.Single(merged.SpecQueues!.Repositories).Schedule.Activated);
        Assert.Equal((SpecRunStatus.Preparing, SpecDependencyMode.WaitForMerge, 1), (dependent.Status, dependent.DependencyModeUsed, dependent.MaxActiveSpecsSlot));
        Assert.Empty(startup.Faults.Concat(stillOpen.Faults).Concat(merged.Faults));
    }

    /// <summary>One coordinator per cycle, like one DI scope per cycle.</summary>
    private RecoveryCoordinator Coordinator()
    {
        ReadyAndMergeFixture f = _fixture;
        InMemoryWorkflowStore store = f.Store;
        var conflicts = new ConflictResolutionRunner(
            store, store, store, f.Git, new ScriptedAgentRunner(), new PromptRenderer(), store, store, f.Ids, f.Clock, new IntegrationOptions(ReadyAndMergeFixture.SkillsRoot));
        var saga = new IntegrationSagaRunner(
            store, store, store, store, store, f.Settings, f.Gate, new IntegrationSagaSteps(store, store, store, f.Git, f.Pulls, f.Issues, conflicts, store, store, f.Clock), store, store, f.Clock);
        var external = new ExternalStateReconciler(
            store,
            store,
            f.Git,
            new IntegrationBranchReconciler(f.Git),
            new TicketGraphReconciler(f.Issues, store, store, store, f.Ids, store, store, f.Clock),
            new FindingIssuanceReconciler(store, store, f.Issues, f.Ids, store, f.Clock),
            new WorktreeReconciler(store, store, f.Git),
            new IntegrationSagaReconciler(store, store, f.Pulls, saga, new RecordingIntegrationLauncher(), store, store, f.Clock, new ExternalReconciliationOptions()),
            new StackBaseReconciler(store, f.Pulls, store, store, f.Clock),
            f.Tracking());
        var scheduler = new SpecQueueScheduler(store, store, f.Issues, f.Settings, store, store, f.Clock);
        var noAgentWork = new RecoveryStageFakes();
        return new RecoveryCoordinator(
            external,
            noAgentWork,
            new CompletionReplay(f),
            new SpecQueueRecomputation(store, scheduler),
            new FrontierReconciliationSignal(store, store, store, f.Clock),
            _gate);
    }

    /// <summary>Outbox replay: delivers pending events to the completion handler and runs the completions it launches.</summary>
    private sealed class CompletionReplay(ReadyAndMergeFixture fixture) : IOutboxReplay
    {
        public async Task<int> ReplayPendingAsync(CancellationToken cancellationToken)
        {
            int pending = (await fixture.Store.ReadPendingAsync(int.MaxValue, cancellationToken)).Count;
            await fixture.DeliverEventsAsync();
            return pending;
        }
    }
}
