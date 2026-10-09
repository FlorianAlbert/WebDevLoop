using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;
using WebDevLoop.Core.Orchestration.Recovery.ExternalState;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Orchestration.Completion.ReadyAndMerge;

namespace WebDevLoop.Core.Tests.Orchestration.Recovery.ExternalState;

/// <summary>
/// A stack-on-top spec stacks its bottom PR on the blocking spec's top stack branch. Once a human squash-merged the blocking
/// stack, that base is stale: reconciliation retargets the bottom PR to trunk, and stack verification accepts a bottom PR
/// on trunk whose blocking stack merged instead of flagging it as needing attention.
/// </summary>
public sealed class StackOnTopBaseReconciliationTests
{
    private const int Blocker = 10;
    private const int BlockerTicket = 11;
    private const int Dependent = 20;
    private const int DependentTicket = 21;

    private readonly ReadyAndMergeFixture _fixture = new();

    [Fact]
    public async Task Bottom_pull_request_is_retargeted_to_trunk_after_the_blocking_stack_was_squash_merged_and_completion_proceeds()
    {
        (SpecRun blocker, SpecRun dependent) = await StackedDependentInTestingAsync();
        SquashMerge(blocker);
        await _fixture.PassTestsAsync(dependent);
        PullStackLayer bottom = _fixture.Layers(dependent)[0];

        IReadOnlyList<ReconciliationAction> actions = await Reconciler().ReconcileAsync(Context(dependent), ReadyAndMergeFixture.Token);
        await _fixture.DeliverEventsAsync();

        Assert.Equal(ReadyAndMergeFixture.Trunk, _fixture.Pulls.Snapshot(bottom.PullRequestNumber).Base);
        Assert.Equal(SpecRunStatus.AwaitingMerge, dependent.Status);
        ReconciliationAction retargeted = Assert.Single(actions);
        Assert.Equal((ReconciliationActionKind.PullRequestBaseRetargeted, bottom.TicketRunId), (retargeted.Kind, retargeted.TicketRunId));
        Assert.Single(_fixture.RunEvents(dependent), runEvent => runEvent.Type == ExternalStateRunEvents.PullRequestBaseRetargeted);
        Assert.Empty(await Reconciler().ReconcileAsync(Context(dependent), ReadyAndMergeFixture.Token));
    }

    [Fact]
    public async Task Bottom_pull_request_that_github_retargeted_to_trunk_after_the_blocking_stack_merged_and_unstacked_passes_verification()
    {
        (SpecRun blocker, SpecRun dependent) = await StackedDependentInTestingAsync();
        SquashMerge(blocker);
        PullStackLayer bottom = _fixture.Layers(dependent)[0];
        _fixture.Pulls.Tamper(bottom.PullRequestNumber, pull => pull with { Base = ReadyAndMergeFixture.Trunk });
        _fixture.Pulls.StacksDissolved = true;
        await _fixture.PassTestsAsync(dependent);

        await _fixture.DeliverEventsAsync();

        Assert.Equal(SpecRunStatus.AwaitingMerge, dependent.Status);
        Assert.Contains(bottom.PullRequestNumber, _fixture.Pulls.MarkedReady);
    }

    [Fact]
    public async Task Bottom_pull_request_on_trunk_while_the_blocking_stack_is_not_merged_still_needs_attention()
    {
        (SpecRun blocker, SpecRun dependent) = await StackedDependentInTestingAsync();
        _fixture.Pulls.Close(_fixture.Layers(blocker)[^1].PullRequestNumber);
        PullStackLayer bottom = _fixture.Layers(dependent)[0];
        _fixture.Pulls.Tamper(bottom.PullRequestNumber, pull => pull with { Base = ReadyAndMergeFixture.Trunk });
        await _fixture.PassTestsAsync(dependent);

        IReadOnlyList<ReconciliationAction> actions = await Reconciler().ReconcileAsync(Context(dependent), ReadyAndMergeFixture.Token);
        await _fixture.DeliverEventsAsync();

        Assert.Empty(actions);
        Assert.Equal(SpecRunStatus.NeedsAttention, dependent.Status);
    }

    [Fact]
    public async Task Bottom_pull_request_is_not_retargeted_while_the_blocking_stack_is_open()
    {
        (_, SpecRun dependent) = await StackedDependentInTestingAsync();
        PullStackLayer bottom = _fixture.Layers(dependent)[0];

        IReadOnlyList<ReconciliationAction> actions = await Reconciler().ReconcileAsync(Context(dependent), ReadyAndMergeFixture.Token);

        Assert.Empty(actions);
        Assert.Equal(bottom.BaseBranch, _fixture.Pulls.Snapshot(bottom.PullRequestNumber).Base);
    }

    /// <summary>
    /// The blocking spec awaits merge with one PR layer; the dependent spec started on top of it (stack-on-top), published
    /// its bottom PR on the blocking spec's top stack branch, and is in <c>Testing</c>.
    /// </summary>
    private async Task<(SpecRun Blocker, SpecRun Dependent)> StackedDependentInTestingAsync()
    {
        _fixture.ConfigureQueue(maxActiveSpecs: 2, SpecDependencyMode.StackOnTop);
        _fixture.SeedSpec(Dependent, Blocker);
        SpecRun blocker = await _fixture.AwaitingMergeSpecAsync(Blocker, BlockerTicket);
        SpecRun dependent = await _fixture.StartAsync(Dependent, DependentTicket);
        Assert.Equal(SpecDependencyMode.StackOnTop, dependent.DependencyModeUsed);
        await _fixture.IntegrateAsync(dependent, DependentTicket);
        Assert.Equal(_fixture.Layers(blocker)[^1].BranchName, _fixture.Layers(dependent)[0].BaseBranch);
        await _fixture.MoveAsync(dependent, SpecRunStatus.ParentReviewing, SpecRunStatus.Testing);
        return (blocker, dependent);
    }

    /// <summary>A human squash-merges the blocking stack: its PRs are merged and trunk gets one new commit that is not a descendant of the layers.</summary>
    private void SquashMerge(SpecRun blocker)
    {
        _fixture.MergePullRequestsOnly(blocker);
        CommitSha trunk = _fixture.Git.RemoteTip(ReadyAndMergeFixture.Trunk)!.Value;
        CommitSha squashed = _fixture.Git.Commit([trunk], $"ticket{BlockerTicket}.cs");
        Assert.Equal(PushOutcome.Pushed, _fixture.Git.PushAsync(_fixture.Location, new RefPush(ReadyAndMergeFixture.Trunk, squashed, trunk), ReadyAndMergeFixture.Token).GetAwaiter().GetResult());
    }

    private StackBaseReconciler Reconciler() =>
        new(_fixture.Store, _fixture.Pulls, _fixture.Store, _fixture.Store, _fixture.Clock);

    private SpecReconciliationContext Context(SpecRun spec) => new(spec, _fixture.Repository);
}
