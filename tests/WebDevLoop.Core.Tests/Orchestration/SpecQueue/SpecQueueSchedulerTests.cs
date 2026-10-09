using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Orchestration.SpecQueue;

public sealed class SpecQueueSchedulerTests
{
    private static readonly SpecRunStatus[] ToAwaitingMerge =
    [
        SpecRunStatus.Running,
        SpecRunStatus.ParentReviewing,
        SpecRunStatus.Testing,
        SpecRunStatus.ReadyForReview,
        SpecRunStatus.AwaitingMerge,
    ];

    private readonly SpecWorkflowFixture _fixture = new();

    [Fact]
    public async Task two_different_specs_cannot_both_become_active_when_max_active_specs_is_1()
    {
        _fixture.ConfigureQueue(maxActiveSpecs: 1);
        _fixture.SeedSpec(1);
        _fixture.SeedSpec(2);
        SpecRun first = await _fixture.EnqueueAsync(1);
        SpecRun second = await _fixture.EnqueueAsync(2);

        SpecScheduleResult pass1 = await _fixture.ScheduleAsync();
        SpecScheduleResult pass2 = await _fixture.ScheduleAsync();

        Assert.Equal([first.Id], pass1.Activated);
        Assert.Empty(pass2.Activated);
        Assert.Equal(SpecRunStatus.Preparing, first.Status);
        Assert.Equal(1, first.MaxActiveSpecsSlot);
        Assert.Equal(SpecRunStatus.Queued, second.Status);
        Assert.Null(second.MaxActiveSpecsSlot);
        Assert.Single(_fixture.Store.PendingEvents.OfType<SpecRunStatusChanged>(), changed => changed.To == SpecRunStatus.Preparing);
    }

    [Fact]
    public async Task independent_specs_can_both_become_active_when_max_active_specs_is_2()
    {
        _fixture.ConfigureQueue(maxActiveSpecs: 2);
        _fixture.SeedSpec(1);
        _fixture.SeedSpec(2);
        _fixture.SeedSpec(3);
        SpecRun first = await _fixture.EnqueueAsync(1);
        SpecRun second = await _fixture.EnqueueAsync(2);
        SpecRun third = await _fixture.EnqueueAsync(3);

        SpecScheduleResult result = await _fixture.ScheduleAsync();

        Assert.Equal([first.Id, second.Id], result.Activated);
        Assert.Equal([1, 2], new[] { first.MaxActiveSpecsSlot, second.MaxActiveSpecsSlot });
        Assert.All([first, second], run => Assert.Equal(SpecRunStatus.Preparing, run.Status));
        Assert.All([first, second], run => Assert.Equal(SpecWorkflowFixture.Trunk, run.BaseBranch));
        Assert.Equal(SpecRunStatus.Queued, third.Status);
    }

    [Fact]
    public async Task a_freed_slot_is_reused_once_the_active_spec_completes()
    {
        _fixture.ConfigureQueue(maxActiveSpecs: 1);
        _fixture.SeedSpec(1);
        _fixture.SeedSpec(2);
        SpecRun first = await _fixture.EnqueueAsync(1);
        SpecRun second = await _fixture.EnqueueAsync(2);
        await _fixture.ScheduleAsync();
        _fixture.Advance(first, [.. ToAwaitingMerge, SpecRunStatus.Completed]);

        SpecScheduleResult result = await _fixture.ScheduleAsync();

        Assert.Equal([second.Id], result.Activated);
        Assert.Equal(1, second.MaxActiveSpecsSlot);
    }

    [Fact]
    public async Task dependent_spec_waits_in_wait_for_merge_mode_while_blocker_is_awaiting_merge()
    {
        _fixture.ConfigureQueue(maxActiveSpecs: 2, SpecDependencyMode.WaitForMerge);
        _fixture.SeedSpec(1);
        _fixture.SeedSpec(2, 1);
        SpecRun blocker = await _fixture.EnqueueAsync(1);
        SpecRun dependent = await _fixture.EnqueueAsync(2);
        await _fixture.ScheduleAsync();
        _fixture.Advance(blocker, ToAwaitingMerge);
        blocker.IntegrationTipSha = _fixture.TrunkTip;

        SpecScheduleResult result = await _fixture.ScheduleAsync();

        Assert.Empty(result.Activated);
        Assert.Equal([dependent.Id], result.Waiting);
        Assert.Equal(SpecRunStatus.WaitingForDependency, dependent.Status);
        Assert.Null(dependent.MaxActiveSpecsSlot);
        Assert.Contains(
            _fixture.Store.PendingEvents.OfType<SpecRunStatusChanged>(),
            changed => changed.SpecRunId == dependent.Id && changed.To == SpecRunStatus.WaitingForDependency);
    }

    [Fact]
    public async Task waiting_dependent_starts_from_trunk_once_the_blocker_is_merged()
    {
        _fixture.ConfigureQueue(maxActiveSpecs: 2, SpecDependencyMode.WaitForMerge);
        _fixture.SeedSpec(1);
        _fixture.SeedSpec(2, 1);
        SpecRun blocker = await _fixture.EnqueueAsync(1);
        SpecRun dependent = await _fixture.EnqueueAsync(2);
        await _fixture.ScheduleAsync();
        _fixture.Advance(blocker, ToAwaitingMerge);
        await _fixture.ScheduleAsync();
        _fixture.Advance(blocker, SpecRunStatus.Completed);

        SpecScheduleResult result = await _fixture.ScheduleAsync();

        Assert.Equal([dependent.Id], result.Activated);
        Assert.Equal(SpecRunStatus.Preparing, dependent.Status);
        Assert.Equal(SpecWorkflowFixture.Trunk, dependent.BaseBranch);
        Assert.Null(dependent.IntegrationBaseSha);
        Assert.Equal(SpecDependencyMode.WaitForMerge, dependent.DependencyModeUsed);
    }

    [Fact]
    public async Task dependent_spec_branches_from_the_blocking_integration_tip_in_stack_on_top_mode()
    {
        _fixture.ConfigureQueue(maxActiveSpecs: 2, SpecDependencyMode.StackOnTop);
        _fixture.SeedSpec(1);
        _fixture.SeedSpec(2, 1);
        SpecRun blocker = await _fixture.EnqueueAsync(1);
        SpecRun dependent = await _fixture.EnqueueAsync(2);
        await _fixture.ScheduleAsync();
        await _fixture.PrepareAsync(blocker);
        CommitSha blockerTip = _fixture.Git.Commit([blocker.IntegrationTipSha!.Value], "feature.txt");
        blocker.IntegrationTipSha = blockerTip;
        _fixture.Advance(blocker, ToAwaitingMerge[1..]);

        SpecScheduleResult result = await _fixture.ScheduleAsync();
        await _fixture.PrepareAsync(dependent);

        Assert.Equal([dependent.Id], result.Activated);
        Assert.Equal(SpecDependencyMode.StackOnTop, dependent.DependencyModeUsed);
        Assert.Equal(SpecWorkflowFixture.Trunk, dependent.BaseBranch);
        Assert.Equal(blockerTip, dependent.IntegrationBaseSha);
        Assert.Equal(blockerTip, await _fixture.Git.GetBranchTipAsync(
            GitRepositoryLocation.From(_fixture.Repository), dependent.IntegrationBranch, GitRefScope.Local, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task stack_on_top_dependent_waits_while_the_blocker_is_still_running()
    {
        _fixture.ConfigureQueue(maxActiveSpecs: 2, SpecDependencyMode.StackOnTop);
        _fixture.SeedSpec(1);
        _fixture.SeedSpec(2, 1);
        SpecRun blocker = await _fixture.EnqueueAsync(1);
        SpecRun dependent = await _fixture.EnqueueAsync(2);
        await _fixture.ScheduleAsync();
        _fixture.Advance(blocker, SpecRunStatus.Running);

        SpecScheduleResult result = await _fixture.ScheduleAsync();

        Assert.Empty(result.Activated);
        Assert.Equal(SpecRunStatus.WaitingForDependency, dependent.Status);
    }

    [Fact]
    public async Task dependency_on_an_open_issue_without_a_run_waits_until_github_closes_it()
    {
        _fixture.ConfigureQueue(maxActiveSpecs: 1, SpecDependencyMode.StackOnTop);
        _fixture.SeedSpec(7);
        _fixture.SeedSpec(2, 7);
        SpecRun dependent = await _fixture.EnqueueAsync(2);

        SpecScheduleResult whileOpen = await _fixture.ScheduleAsync();
        await _fixture.Issues.CloseAsync(SpecWorkflowFixture.Issue(7), IssueCloseReason.Completed, TestContext.Current.CancellationToken);
        SpecScheduleResult afterClose = await _fixture.ScheduleAsync();

        Assert.Equal([dependent.Id], whileOpen.Waiting);
        Assert.Equal([dependent.Id], afterClose.Activated);
        Assert.Equal(SpecRunStatus.Preparing, dependent.Status);
    }

    [Fact]
    public async Task independent_spec_behind_a_waiting_dependent_still_starts_when_a_slot_is_free()
    {
        _fixture.ConfigureQueue(maxActiveSpecs: 2, SpecDependencyMode.WaitForMerge);
        _fixture.SeedSpec(7);
        _fixture.SeedSpec(2, 7);
        _fixture.SeedSpec(3);
        SpecRun dependent = await _fixture.EnqueueAsync(2);
        SpecRun independent = await _fixture.EnqueueAsync(3);

        SpecScheduleResult result = await _fixture.ScheduleAsync();

        Assert.Equal([independent.Id], result.Activated);
        Assert.Equal(SpecRunStatus.WaitingForDependency, dependent.Status);
        Assert.Null(independent.DependencyModeUsed);
    }

    [Fact]
    public async Task losing_the_activation_race_reports_a_conflict_without_publishing_events()
    {
        _fixture.ConfigureQueue(maxActiveSpecs: 1);
        _fixture.SeedSpec(1);
        await _fixture.EnqueueAsync(1);
        int eventsBefore = _fixture.Store.PendingEvents.Count();
        _fixture.Store.ConflictOnNextSave = true;

        SpecScheduleResult result = await _fixture.ScheduleAsync();

        Assert.True(result.ConcurrencyConflict);
        Assert.Empty(result.Activated);
        Assert.Equal(eventsBefore, _fixture.Store.PendingEvents.Count());
    }

    [Fact]
    public async Task disabled_repository_is_not_scheduled()
    {
        _fixture.SeedSpec(1);
        SpecRun run = await _fixture.EnqueueAsync(1);
        _fixture.Repository.SetEnabled(false, SpecWorkflowFixture.T0);

        SpecScheduleResult result = await _fixture.ScheduleAsync();

        Assert.Empty(result.Activated);
        Assert.Equal(SpecRunStatus.Queued, run.Status);
    }
}
