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

    public SpecQueueSchedulerTests()
    {
        foreach (int spec in new[] { 1, 2, 3, 7 })
        {
            _fixture.SeedTicket(100 + spec, spec);
        }
    }

    [Fact]
    public async Task a_blocker_with_an_unrecognized_state_holds_the_spec_until_explicitly_closed()
    {
        _fixture.SeedSpec(1);
        var blocker = new IssueRef("elsewhere", "library", 900);
        const IssueState unrecognizedState = (IssueState)2;
        _fixture.Issues.Seed(new IssueSnapshot(blocker, "Blocker", "", unrecognizedState, []));
        _fixture.Issues.SetBlockedBy(SpecWorkflowFixture.Issue(1), blocker);
        SpecRun run = await _fixture.EnqueueAsync(1);

        SpecScheduleResult blocked = await _fixture.ScheduleAsync();

        Assert.Empty(blocked.Activated);
        Assert.Equal([run.Id], blocked.Waiting);
        Assert.Null(run.MaxActiveSpecsSlot);
        await _fixture.Issues.CloseAsync(blocker, IssueCloseReason.Completed, TestContext.Current.CancellationToken);

        SpecScheduleResult released = await _fixture.ScheduleAsync();

        Assert.Equal([run.Id], released.Activated);
    }

    [Theory]
    [InlineData("parent unlabeled", false, "requires the 'webdevloop' label")]
    [InlineData("parent closed", false, "must be open")]
    [InlineData("tickets unlabeled", false, "at least one open sub-issue")]
    [InlineData("parent unlabeled", true, "requires the 'webdevloop' label")]
    [InlineData("parent closed", true, "must be open")]
    [InlineData("tickets unlabeled", true, "at least one open sub-issue")]
    public async Task current_eligibility_defers_queued_and_dependency_waiting_specs_without_holding_a_slot(
        string change, bool waitingForDependency, string expectedReason)
    {
        IssueSnapshot parent = _fixture.SeedSpec(1);
        IssueSnapshot child = await _fixture.Issues.GetIssueAsync(SpecWorkflowFixture.Issue(101), TestContext.Current.CancellationToken);
        SpecRun run = await _fixture.EnqueueAsync(1);
        if (waitingForDependency)
        {
            _fixture.SeedSpec(7);
            _fixture.Issues.SetBlockedBy(parent.Ref, SpecWorkflowFixture.Issue(7));
            await _fixture.ScheduleAsync();
            await _fixture.Issues.CloseAsync(SpecWorkflowFixture.Issue(7), IssueCloseReason.Completed, TestContext.Current.CancellationToken);
        }

        _fixture.SeedSpec(2);
        SpecRun independent = await _fixture.EnqueueAsync(2);
        _fixture.Issues.Seed(change switch
        {
            "parent unlabeled" => parent with { Labels = ["ready-for-agent"] },
            "parent closed" => parent with { State = IssueState.Closed },
            _ => child with { Labels = ["ready-for-agent"] },
        });

        SpecScheduleResult deferred = await _fixture.ScheduleAsync();

        Assert.Equal([independent.Id], deferred.Activated);
        Assert.Contains(run.Id, deferred.Waiting);
        Assert.Equal(waitingForDependency ? SpecRunStatus.WaitingForDependency : SpecRunStatus.Queued, run.Status);
        Assert.Null(run.MaxActiveSpecsSlot);
        Assert.Null(run.StartedAt);
        Assert.Contains(expectedReason, run.WorkDecisionReason);
        Assert.DoesNotContain(_fixture.Store.PendingEvents.OfType<SpecRunStatusChanged>(),
            changed => changed.SpecRunId == run.Id && changed.To == SpecRunStatus.Preparing);

        _fixture.Issues.Seed(parent);
        _fixture.Issues.Seed(child);
        _fixture.ConfigureQueue(maxActiveSpecs: 2);

        Assert.Equal([run.Id], (await _fixture.ScheduleAsync()).Activated);
        Assert.Null(run.WorkDecisionReason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task restored_eligibility_clears_the_opt_out_reason_even_when_the_run_cannot_start(bool stillBlocked)
    {
        IssueSnapshot parent = _fixture.SeedSpec(1);
        _fixture.SeedSpec(7);
        _fixture.Issues.SetBlockedBy(parent.Ref, SpecWorkflowFixture.Issue(7));
        SpecRun run = await _fixture.EnqueueAsync(1);
        await _fixture.ScheduleAsync();
        _fixture.Issues.Seed(parent with { Labels = [] });
        _fixture.SeedSpec(2);
        await _fixture.EnqueueAsync(2);
        await _fixture.ScheduleAsync();
        Assert.Contains("requires the 'webdevloop' label", run.WorkDecisionReason);
        _fixture.Issues.Seed(parent with { BlockedBy = stillBlocked ? [SpecWorkflowFixture.Issue(7)] : [] });

        SpecScheduleResult result = await _fixture.ScheduleAsync();

        Assert.Empty(result.Activated);
        Assert.Null(run.MaxActiveSpecsSlot);
        Assert.Null(run.WorkDecisionReason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task unavailable_parent_or_blocker_defers_only_its_spec_and_an_independent_spec_behind_it_starts(bool parentUnavailable)
    {
        _fixture.SeedSpec(1, 7);
        _fixture.SeedSpec(2);
        _fixture.SeedSpec(7);
        SpecRun deferred = await _fixture.EnqueueAsync(1);
        SpecRun independent = await _fixture.EnqueueAsync(2);
        _fixture.Issues.RemoveIssue(SpecWorkflowFixture.Issue(parentUnavailable ? 1 : 7));

        SpecScheduleResult result = await _fixture.ScheduleAsync();

        Assert.Equal([independent.Id], result.Activated);
        Assert.Equal(SpecRunStatus.Queued, deferred.Status);
        Assert.Null(deferred.MaxActiveSpecsSlot);
        SpecScheduleFault fault = Assert.Single(result.Faults);
        Assert.Equal(deferred.Id, fault.SpecRunId);
        Assert.Contains("Check issue access", fault.Error);
    }

    [Fact]
    public async Task parent_blockers_added_and_removed_after_enqueue_control_each_scheduling_decision()
    {
        _fixture.SeedSpec(1);
        _fixture.SeedSpec(7);
        SpecRun run = await _fixture.EnqueueAsync(1);
        _fixture.Issues.SetBlockedBy(SpecWorkflowFixture.Issue(1), SpecWorkflowFixture.Issue(7));

        SpecScheduleResult blocked = await _fixture.ScheduleAsync();

        Assert.Equal([run.Id], blocked.Waiting);
        Assert.Empty(blocked.Activated);
        _fixture.Issues.SetBlockedBy(SpecWorkflowFixture.Issue(1));

        SpecScheduleResult released = await _fixture.ScheduleAsync();

        Assert.Equal([run.Id], released.Activated);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task every_external_and_cross_repository_blocker_must_close_regardless_of_queue_order(bool blockerFirst)
    {
        _fixture.ConfigureQueue(maxActiveSpecs: 2);
        var foreign = new IssueRef("elsewhere", "other", 7);
        _fixture.SeedSpec(7);
        _fixture.Issues.Seed(foreign, "External blocker");
        _fixture.SeedSpec(1, 7);
        _fixture.Issues.SetBlockedBy(SpecWorkflowFixture.Issue(1), SpecWorkflowFixture.Issue(7), foreign);
        SpecRun? blocker = blockerFirst ? await _fixture.EnqueueAsync(7) : null;
        SpecRun dependent = await _fixture.EnqueueAsync(1);
        blocker ??= await _fixture.EnqueueAsync(7);
        await _fixture.ScheduleAsync();
        await _fixture.Issues.CloseAsync(blocker.ParentIssue, IssueCloseReason.Completed, TestContext.Current.CancellationToken);

        SpecScheduleResult oneOpen = await _fixture.ScheduleAsync();

        Assert.Empty(oneOpen.Activated);
        Assert.Equal([dependent.Id], oneOpen.Waiting);
        await _fixture.Issues.CloseAsync(foreign, IssueCloseReason.Completed, TestContext.Current.CancellationToken);

        SpecScheduleResult allClosed = await _fixture.ScheduleAsync();

        Assert.Equal([dependent.Id], allClosed.Activated);
    }

    [Fact]
    public async Task closed_unmerged_blocker_releases_a_spec_from_the_latest_trunk_not_the_blocker_tip()
    {
        _fixture.SeedSpec(7);
        _fixture.SeedSpec(1, 7);
        _fixture.SeedTicket(11, spec: 1);
        SpecRun run = await _fixture.EnqueueAsync(1);
        await _fixture.ScheduleAsync();
        CommitSha currentTrunk = _fixture.Git.SeedRemoteBranch(SpecWorkflowFixture.Trunk, "new-trunk.txt");
        await _fixture.Issues.CloseAsync(SpecWorkflowFixture.Issue(7), IssueCloseReason.Completed, TestContext.Current.CancellationToken);

        await _fixture.ScheduleAsync();
        await _fixture.PrepareAsync(run);

        Assert.Equal(currentTrunk, run.IntegrationBaseSha);
        Assert.Equal(currentTrunk, await _fixture.Git.GetBranchTipAsync(
            GitRepositoryLocation.From(_fixture.Repository), run.IntegrationBranch, GitRefScope.Local, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task unavailable_parent_or_blocker_defers_start_and_queue_recovery_can_retry(bool parentUnavailable)
    {
        _fixture.SeedSpec(7);
        _fixture.SeedSpec(1, 7);
        SpecRun run = await _fixture.EnqueueAsync(1);
        await _fixture.Issues.CloseAsync(SpecWorkflowFixture.Issue(7), IssueCloseReason.Completed, TestContext.Current.CancellationToken);
        IssueRef missing = SpecWorkflowFixture.Issue(parentUnavailable ? 1 : 7);
        _fixture.Issues.RemoveIssue(missing);

        SpecScheduleResult result = await _fixture.ScheduleAsync();
        SpecScheduleFault error = Assert.Single(result.Faults);

        Assert.Equal(run.Id, error.SpecRunId);
        Assert.Contains(missing.ToString(), error.Error);
        Assert.Contains("Check issue access", error.Error);
        Assert.Contains("retry queue recovery", error.Error);
        Assert.Equal(SpecRunStatus.Queued, run.Status);
        Assert.Null(run.MaxActiveSpecsSlot);
        if (parentUnavailable)
        {
            _fixture.SeedSpec(1, 7);
        }
        else
        {
            _fixture.SeedSpec(7);
        }
        await _fixture.Issues.CloseAsync(SpecWorkflowFixture.Issue(7), IssueCloseReason.Completed, TestContext.Current.CancellationToken);

        Assert.Equal([run.Id], (await _fixture.ScheduleAsync()).Activated);
    }

    [Fact]
    public async Task a_label_lookup_failure_returns_a_diagnostic_without_claiming_work_and_can_be_retried()
    {
        _fixture.SeedSpec(1);
        SpecRun run = await _fixture.EnqueueAsync(1);
        _fixture.Labels.LookupFailures[SpecWorkflowFixture.Repo] = new RepositoryLabelException("Check Issues permissions and retry.");

        SpecScheduleResult deferred = await _fixture.ScheduleAsync();

        Assert.Contains("Check Issues permissions and retry.", Assert.IsType<GitHubIssuesException>(deferred.LookupError).Message);
        Assert.Empty(deferred.Activated);
        Assert.Equal(SpecRunStatus.Queued, run.Status);
        Assert.Null(run.MaxActiveSpecsSlot);
        _fixture.Labels.LookupFailures.Clear();

        SpecScheduleResult recovered = await _fixture.ScheduleAsync();

        Assert.Null(recovered.LookupError);
        Assert.Equal([run.Id], recovered.Activated);
    }

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
    public async Task open_parent_blocker_holds_a_spec_while_its_run_is_awaiting_merge()
    {
        _fixture.ConfigureQueue(maxActiveSpecs: 2);
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
    public async Task completed_run_does_not_release_its_open_issue_but_closure_does()
    {
        _fixture.ConfigureQueue(maxActiveSpecs: 2);
        _fixture.SeedSpec(1);
        _fixture.SeedSpec(2, 1);
        SpecRun blocker = await _fixture.EnqueueAsync(1);
        SpecRun dependent = await _fixture.EnqueueAsync(2);
        await _fixture.ScheduleAsync();
        _fixture.Advance(blocker, ToAwaitingMerge);
        await _fixture.ScheduleAsync();
        _fixture.Advance(blocker, SpecRunStatus.Completed);

        SpecScheduleResult stillOpen = await _fixture.ScheduleAsync();
        Assert.Empty(stillOpen.Activated);
        Assert.Equal([dependent.Id], stillOpen.Waiting);
        await _fixture.Issues.CloseAsync(blocker.ParentIssue, IssueCloseReason.Completed, TestContext.Current.CancellationToken);
        SpecScheduleResult result = await _fixture.ScheduleAsync();

        Assert.Equal([dependent.Id], result.Activated);
        Assert.Equal(SpecRunStatus.Preparing, dependent.Status);
        Assert.Equal(SpecWorkflowFixture.Trunk, dependent.BaseBranch);
        Assert.Null(dependent.IntegrationBaseSha);
    }

    [Fact]
    public async Task open_parent_blocker_holds_a_spec_while_its_run_is_running()
    {
        _fixture.ConfigureQueue(maxActiveSpecs: 2);
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
        _fixture.ConfigureQueue(maxActiveSpecs: 1);
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
        _fixture.ConfigureQueue(maxActiveSpecs: 2);
        _fixture.SeedSpec(7);
        _fixture.SeedSpec(2, 7);
        _fixture.SeedSpec(3);
        SpecRun dependent = await _fixture.EnqueueAsync(2);
        SpecRun independent = await _fixture.EnqueueAsync(3);

        SpecScheduleResult result = await _fixture.ScheduleAsync();

        Assert.Equal([independent.Id], result.Activated);
        Assert.Equal(SpecRunStatus.WaitingForDependency, dependent.Status);
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
    public async Task ready_for_review_spec_does_not_block_an_independent_spec_when_max_active_specs_is_1()
    {
        _fixture.ConfigureQueue(maxActiveSpecs: 1);
        _fixture.SeedSpec(1);
        _fixture.SeedSpec(2);
        SpecRun first = await _fixture.EnqueueAsync(1);
        SpecRun second = await _fixture.EnqueueAsync(2);
        await _fixture.ScheduleAsync();
        _fixture.Advance(first, ToAwaitingMerge[..^1]);

        SpecScheduleResult result = await _fixture.ScheduleAsync();

        Assert.Equal([second.Id], result.Activated);
        Assert.Equal(SpecRunStatus.Preparing, second.Status);
        Assert.Equal(1, second.MaxActiveSpecsSlot);
        Assert.Equal(SpecWorkflowFixture.Trunk, second.BaseBranch);
        Assert.Null(second.IntegrationBaseSha);
        Assert.Null(first.MaxActiveSpecsSlot);
        Assert.False(first.IsTerminal);
    }

    [Fact]
    public async Task awaiting_merge_spec_does_not_block_an_independent_spec_when_max_active_specs_is_1()
    {
        _fixture.ConfigureQueue(maxActiveSpecs: 1);
        _fixture.SeedSpec(1);
        _fixture.SeedSpec(2);
        SpecRun first = await _fixture.EnqueueAsync(1);
        SpecRun second = await _fixture.EnqueueAsync(2);
        await _fixture.ScheduleAsync();
        _fixture.Advance(first, ToAwaitingMerge);

        SpecScheduleResult result = await _fixture.ScheduleAsync();

        Assert.Equal([second.Id], result.Activated);
    }

    [Fact]
    public async Task parent_issue_closure_releases_a_waiting_spec_with_one_active_slot()
    {
        _fixture.ConfigureQueue(maxActiveSpecs: 1);
        _fixture.SeedSpec(1);
        _fixture.SeedSpec(2, 1);
        SpecRun blocker = await _fixture.EnqueueAsync(1);
        SpecRun dependent = await _fixture.EnqueueAsync(2);
        await _fixture.ScheduleAsync();
        _fixture.Advance(blocker, ToAwaitingMerge);

        SpecScheduleResult whileUnmerged = await _fixture.ScheduleAsync();
        _fixture.Advance(blocker, SpecRunStatus.Completed);
        await _fixture.Issues.CloseAsync(blocker.ParentIssue, IssueCloseReason.Completed, TestContext.Current.CancellationToken);
        SpecScheduleResult afterMerge = await _fixture.ScheduleAsync();

        Assert.Empty(whileUnmerged.Activated);
        Assert.Equal([dependent.Id], whileUnmerged.Waiting);
        Assert.Equal([dependent.Id], afterMerge.Activated);
        Assert.Equal(SpecWorkflowFixture.Trunk, dependent.BaseBranch);
    }

    [Fact]
    public async Task open_parent_blocker_holds_a_spec_when_its_run_needs_attention()
    {
        _fixture.ConfigureQueue(maxActiveSpecs: 1);
        _fixture.SeedSpec(1);
        _fixture.SeedTicket(11, spec: 1);
        _fixture.SeedSpec(2, 1);
        _fixture.SeedTicket(12, spec: 2);
        SpecRun blocker = await _fixture.EnqueueAsync(1);
        SpecRun dependent = await _fixture.EnqueueAsync(2);
        await _fixture.ScheduleAsync();
        _fixture.Advance(blocker, ToAwaitingMerge);
        blocker.MarkNeedsAttention(AttentionReasons.Unclassified("closed unmerged", true), _fixture.Clock.UtcNow);

        SpecScheduleResult result = await _fixture.ScheduleAsync();

        Assert.Empty(result.Activated);
        Assert.Equal(SpecRunStatus.WaitingForDependency, dependent.Status);
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
