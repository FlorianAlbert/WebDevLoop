using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Orchestration.Completion.ReadyAndMerge;

public sealed class MergeTrackingTests
{
    private readonly ReadyAndMergeFixture _fixture = new();

    private static CancellationToken Token => ReadyAndMergeFixture.Token;

    [Fact]
    public async Task A_ready_stack_blocks_a_wait_for_merge_dependent_until_merged_and_the_dependent_then_branches_from_the_updated_trunk()
    {
        _fixture.SeedSpec(10);
        _fixture.SeedSpec(20, blockedBySpecs: 10);
        SpecRun blocking = await _fixture.TestedSpecAsync(10, 11, 12);
        SpecRun dependent = await _fixture.EnqueueAsync(20, 21);
        await _fixture.DeliverEventsAsync();

        await _fixture.ScheduleAsync();
        MergeTrackingPass open = await _fixture.Tracking().TrackAllAsync(Token);
        await _fixture.ScheduleAsync();
        SpecRunStatus whileOpen = dependent.Status;
        CommitSha trunk = await _fixture.MergeStackAsync(blocking);
        MergeTrackingPass merged = await _fixture.Tracking().TrackAllAsync(Token);
        await _fixture.ScheduleAsync();
        await _fixture.PrepareAsync(dependent);

        Assert.Equal(MergeTrackingOutcome.Open, open.Merges[blocking.Id].Outcome);
        Assert.Equal(SpecRunStatus.WaitingForDependency, whileOpen);
        Assert.Equal(MergeTrackingOutcome.Completed, merged.Merges[blocking.Id].Outcome);
        Assert.Equal(SpecRunStatus.Completed, blocking.Status);
        Assert.Equal(_fixture.Clock.UtcNow, blocking.CompletedAt);
        Assert.Contains((SpecRunStatus.AwaitingMerge, SpecRunStatus.Completed), _fixture.Transitions(blocking));
        Assert.Equal(SpecRunStatus.Running, dependent.Status);
        Assert.Equal(SpecDependencyMode.WaitForMerge, dependent.DependencyModeUsed);
        Assert.Equal(trunk, dependent.IntegrationBaseSha);
        Assert.Equal(trunk, await _fixture.Git.GetBranchTipAsync(_fixture.Location, dependent.IntegrationBranch, GitRefScope.Local, Token));
    }

    [Fact]
    public async Task A_stack_closed_unmerged_needs_attention_and_keeps_its_dependent_waiting()
    {
        _fixture.SeedSpec(10);
        _fixture.SeedSpec(20, blockedBySpecs: 10);
        SpecRun blocking = await _fixture.AwaitingMergeSpecAsync(10, 11, 12);
        SpecRun dependent = await _fixture.EnqueueAsync(20, 21);
        PullStackLayer closed = _fixture.Layers(blocking)[0];
        _fixture.Pulls.Close(closed.PullRequestNumber);

        MergeTrackingResult result = await _fixture.Tracking().TrackAsync(blocking.Id, Token);
        await _fixture.ScheduleAsync();

        Assert.Equal(MergeTrackingOutcome.NeedsAttention, result.Outcome);
        Assert.Equal(SpecRunStatus.NeedsAttention, blocking.Status);
        Assert.Contains("closed without being merged", blocking.FailureReason, StringComparison.Ordinal);
        Assert.Equal(SpecRunStatus.WaitingForDependency, dependent.Status);
    }

    [Fact]
    public async Task Merged_prs_whose_top_layer_never_reaches_trunk_need_attention_after_the_timeout()
    {
        _fixture.Options = new ReadyAndMergeOptions { TrunkContainmentTimeout = TimeSpan.FromMinutes(30) };
        SpecRun spec = await _fixture.AwaitingMergeSpecAsync(10, 11, 12);
        _fixture.MergePullRequestsOnly(spec);

        MergeTrackingResult first = await _fixture.Tracking().TrackAsync(spec.Id, Token);
        _fixture.Clock.Advance(TimeSpan.FromMinutes(29));
        MergeTrackingResult beforeTimeout = await _fixture.Tracking().TrackAsync(spec.Id, Token);
        _fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        MergeTrackingResult afterTimeout = await _fixture.Tracking().TrackAsync(spec.Id, Token);

        Assert.Equal(MergeTrackingOutcome.AwaitingTrunk, first.Outcome);
        Assert.Equal(MergeTrackingOutcome.AwaitingTrunk, beforeTimeout.Outcome);
        SpecStackAwaitingTrunk waiting = Assert.Single(_fixture.EventsOf<SpecStackAwaitingTrunk>(spec));
        Assert.Equal(_fixture.Layers(spec)[^1].PullRequestNumber, waiting.TopPullRequest);
        Assert.Equal(MergeTrackingOutcome.NeedsAttention, afterTimeout.Outcome);
        Assert.Equal(SpecRunStatus.NeedsAttention, spec.Status);
        Assert.Contains("does not contain", spec.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Trunk_catching_up_with_merged_prs_before_the_timeout_completes_the_spec()
    {
        SpecRun spec = await _fixture.AwaitingMergeSpecAsync(10, 11);
        _fixture.MergePullRequestsOnly(spec);
        MergeTrackingResult waiting = await _fixture.Tracking().TrackAsync(spec.Id, Token);

        await _fixture.MergeStackAsync(spec);
        MergeTrackingResult merged = await _fixture.Tracking().TrackAsync(spec.Id, Token);

        Assert.Equal(MergeTrackingOutcome.AwaitingTrunk, waiting.Outcome);
        Assert.Equal(MergeTrackingOutcome.Completed, merged.Outcome);
        Assert.Equal(SpecRunStatus.Completed, spec.Status);
    }

    [Fact]
    public async Task A_failing_poll_of_one_spec_does_not_stop_tracking_the_others()
    {
        SpecRun failing = await _fixture.AwaitingMergeSpecAsync(10, 11);
        SpecRun merging = await _fixture.AwaitingMergeSpecAsync(20, 21);
        PullRequestNumber failingPull = _fixture.Layers(failing)[0].PullRequestNumber;
        _fixture.Pulls.FailMergeStatusWhen = stack => stack.Contains(failingPull);
        await _fixture.MergeStackAsync(merging);

        MergeTrackingPass pass = await _fixture.Tracking().TrackAllAsync(Token);

        Assert.Equal(MergeTrackingOutcome.Faulted, pass.Merges[failing.Id].Outcome);
        Assert.Contains("GitHub is unavailable", pass.Merges[failing.Id].Reason, StringComparison.Ordinal);
        Assert.Equal(SpecRunStatus.AwaitingMerge, failing.Status);
        Assert.Equal(MergeTrackingOutcome.Completed, pass.Merges[merging.Id].Outcome);
        Assert.Equal(SpecRunStatus.Completed, merging.Status);
    }

    [Fact]
    public async Task Specs_that_are_not_awaiting_merge_are_not_polled()
    {
        SpecRun running = await _fixture.StartAsync(10, 11);

        MergeTrackingResult result = await _fixture.Tracking().TrackAsync(running.Id, Token);
        MergeTrackingPass pass = await _fixture.Tracking().TrackAllAsync(Token);

        Assert.Equal(MergeTrackingOutcome.NotTracked, result.Outcome);
        Assert.Empty(pass.Merges);
        Assert.Equal(SpecRunStatus.Running, running.Status);
    }
}
