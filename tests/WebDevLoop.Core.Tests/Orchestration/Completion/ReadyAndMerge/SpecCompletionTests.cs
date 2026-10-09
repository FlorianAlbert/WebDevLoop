using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;
using WebDevLoop.Core.Orchestration.Completion.Testing;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Orchestration.Completion.ReadyAndMerge;

public sealed class SpecCompletionTests
{
    private readonly ReadyAndMergeFixture _fixture = new();

    private static CancellationToken Token => ReadyAndMergeFixture.Token;

    [Fact]
    public async Task Passing_tests_mark_every_draft_pr_of_the_verified_stack_ready_and_the_spec_awaits_merge()
    {
        SpecRun spec = await _fixture.TestedSpecAsync(10, 11, 12);
        IReadOnlyList<PullStackLayer> layers = _fixture.Layers(spec);
        string[] worktrees = [_fixture.Ticket(spec, 11).WorktreePath!, _fixture.Ticket(spec, 12).WorktreePath!];

        await _fixture.DeliverEventsAsync();

        Assert.Equal(CompletionOutcome.AwaitingMerge, Assert.Single(_fixture.Completions).Result.Outcome);
        Assert.Equal(layers.Select(layer => layer.PullRequestNumber), _fixture.Pulls.MarkedReady);
        Assert.All(layers, layer => Assert.False(layer.IsDraft));
        Assert.All(layers, layer => Assert.False(_fixture.Pulls.Snapshot(layer.PullRequestNumber).IsDraft));
        Assert.Equal(SpecRunStatus.AwaitingMerge, spec.Status);
        Assert.Equal(ReadyAndMergeFixture.T0, spec.ReadyAt);
        Assert.Equal(
            [(SpecRunStatus.Testing, SpecRunStatus.ReadyForReview), (SpecRunStatus.ReadyForReview, SpecRunStatus.AwaitingMerge)],
            _fixture.Transitions(spec).SkipWhile(transition => transition.To != SpecRunStatus.ReadyForReview));
        SpecCompletionReported reported = Assert.Single(_fixture.EventsOf<SpecCompletionReported>(spec));
        Assert.Equal((spec.IntegrationBranch, layers[^1].CommitSha, 2), (reported.IntegrationBranch, reported.IntegrationTip, reported.PullRequestCount));
        Assert.All(worktrees, path => Assert.False(_fixture.WorktreeExists(path)));
        SpecWorktreesCleanedUp cleaned = Assert.Single(_fixture.EventsOf<SpecWorktreesCleanedUp>(spec));
        Assert.Equal((2, 0), (cleaned.Removed, cleaned.Warnings.Count));
    }

    [Fact]
    public async Task The_single_pr_of_a_one_layer_stack_is_marked_ready_too()
    {
        SpecRun spec = await _fixture.TestedSpecAsync(10, 11);

        await _fixture.DeliverEventsAsync();

        PullStackLayer layer = Assert.Single(_fixture.Layers(spec));
        Assert.Equal([layer.PullRequestNumber], _fixture.Pulls.MarkedReady);
        Assert.Equal(SpecRunStatus.AwaitingMerge, spec.Status);
    }

    [Fact]
    public async Task Ready_for_review_releases_the_active_slot_so_the_next_independent_spec_starts_from_trunk()
    {
        SpecRun ready = await _fixture.TestedSpecAsync(10, 11);
        SpecRun next = await _fixture.EnqueueAsync(20, 21);
        await _fixture.ScheduleAsync();
        Assert.Equal(SpecRunStatus.Queued, next.Status);

        await _fixture.DeliverEventsAsync();
        await _fixture.ScheduleAsync();

        Assert.Equal(SpecRunStatus.AwaitingMerge, ready.Status);
        Assert.Null(ready.MaxActiveSpecsSlot);
        Assert.Equal(SpecRunStatus.Preparing, next.Status);
        Assert.Equal(1, next.MaxActiveSpecsSlot);
        Assert.Null(next.DependencyModeUsed);
    }

    [Fact]
    public async Task A_re_emitted_testing_pass_is_ignored_once_the_stack_is_ready()
    {
        SpecRun spec = await _fixture.TestedSpecAsync(10, 11, 12);
        await _fixture.DeliverEventsAsync();

        _fixture.Store.Append(new SpecTestingPassed(spec.Id, spec.RepositoryId, spec.TestCycle, _fixture.Clock.UtcNow));
        await _fixture.SaveAsync();
        await _fixture.DeliverEventsAsync();

        Assert.Equal(2, _fixture.Launcher.Launched.Count);
        Assert.Equal(CompletionOutcome.NothingToDo, _fixture.Completions[^1].Result.Outcome);
        Assert.Equal(2, _fixture.Pulls.MarkedReady.Count);
        Assert.Single(_fixture.Transitions(spec), transition => transition.To == SpecRunStatus.ReadyForReview);
        Assert.Single(_fixture.EventsOf<SpecCompletionReported>(spec));
        Assert.Equal(SpecRunStatus.AwaitingMerge, spec.Status);
    }

    [Fact]
    public async Task A_testing_spec_without_a_passing_verdict_of_its_current_cycle_is_not_completed()
    {
        SpecRun spec = await _fixture.StartAsync(10, 11);
        await _fixture.IntegrateAsync(spec, 11);
        CompletionResult running = await _fixture.Completion().RunAsync(new CompletionAssignment(spec.Id), Token);
        await _fixture.MoveAsync(spec, SpecRunStatus.ParentReviewing, SpecRunStatus.Testing);

        CompletionResult untested = await _fixture.Completion().RunAsync(new CompletionAssignment(spec.Id), Token);

        Assert.Equal(CompletionOutcome.NothingToDo, running.Outcome);
        Assert.Equal(CompletionOutcome.NothingToDo, untested.Outcome);
        Assert.Equal(SpecRunStatus.Testing, spec.Status);
        Assert.Empty(_fixture.Pulls.MarkedReady);
    }

    public enum StackDamage
    {
        PullRequestHeadMoved,
        PullRequestRetargeted,
        PullRequestClosed,
        StackOrderChanged,
        UntestedIntegrationCommit,
    }

    [Theory]
    [InlineData(StackDamage.PullRequestHeadMoved, "head commit")]
    [InlineData(StackDamage.PullRequestRetargeted, "targets")]
    [InlineData(StackDamage.PullRequestClosed, "is Closed")]
    [InlineData(StackDamage.StackOrderChanged, "GitHub stack")]
    [InlineData(StackDamage.UntestedIntegrationCommit, "not tested")]
    public async Task A_stack_that_fails_verification_needs_attention_before_any_pr_is_marked_ready(StackDamage damage, string reasonFragment)
    {
        SpecRun spec = await _fixture.TestedSpecAsync(10, 11, 12);
        IReadOnlyList<PullStackLayer> layers = _fixture.Layers(spec);
        await DamageAsync(spec, layers, damage);
        string worktree = _fixture.Ticket(spec, 12).WorktreePath!;

        await _fixture.DeliverEventsAsync();

        CompletionResult result = Assert.Single(_fixture.Completions).Result;
        Assert.Equal(CompletionOutcome.NeedsAttention, result.Outcome);
        Assert.Contains(reasonFragment, result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SpecRunStatus.NeedsAttention, spec.Status);
        Assert.Equal(result.Reason, spec.FailureReason);
        Assert.Empty(_fixture.Pulls.MarkedReady);
        Assert.All(layers, layer => Assert.True(layer.IsDraft));
        Assert.True(_fixture.WorktreeExists(worktree));
        Assert.Empty(_fixture.EventsOf<SpecCompletionReported>(spec));
    }

    [Fact]
    public async Task A_tested_integration_commit_that_no_pr_layer_publishes_needs_attention()
    {
        SpecRun spec = await _fixture.StartAsync(10, 11);
        await _fixture.IntegrateAsync(spec, 11);
        await AdvanceIntegrationBranchAsync(spec);
        await _fixture.MoveAsync(spec, SpecRunStatus.ParentReviewing, SpecRunStatus.Testing);
        await _fixture.PassTestsAsync(spec);

        await _fixture.DeliverEventsAsync();

        CompletionResult result = Assert.Single(_fixture.Completions).Result;
        Assert.Equal(CompletionOutcome.NeedsAttention, result.Outcome);
        Assert.Contains("top layer", result.Reason, StringComparison.Ordinal);
        Assert.Empty(_fixture.Pulls.MarkedReady);
    }

    [Fact]
    public async Task Without_pull_requests_the_integrated_tickets_are_closed_and_the_pushed_integration_branch_is_reported()
    {
        SpecRun spec = await _fixture.StartAsync(10, 11, 12);
        await _fixture.IntegrateWithoutPullRequestAsync(spec, 11);
        await _fixture.IntegrateWithoutPullRequestAsync(spec, 12);
        await _fixture.MoveAsync(spec, SpecRunStatus.ParentReviewing, SpecRunStatus.Testing);
        await _fixture.PassTestsAsync(spec);
        string worktree = _fixture.Ticket(spec, 11).WorktreePath!;
        CommitSha tip = spec.IntegrationTipSha!.Value;

        await _fixture.DeliverEventsAsync();

        Assert.Equal(CompletionOutcome.CompletedWithoutPullRequests, Assert.Single(_fixture.Completions).Result.Outcome);
        Assert.Equal(IssueState.Closed, _fixture.IssueState(11));
        Assert.Equal(IssueState.Closed, _fixture.IssueState(12));
        Assert.Equal(IssueState.Open, _fixture.IssueState(10));
        Assert.Equal(tip, _fixture.Git.RemoteTip(spec.IntegrationBranch));
        (IssueRef issue, string body) = Assert.Single(_fixture.Issues.Comments);
        Assert.Equal(10, issue.Number);
        Assert.Contains(spec.IntegrationBranch.Value, body, StringComparison.Ordinal);
        Assert.Contains(tip.Value, body, StringComparison.Ordinal);
        SpecCompletionReported reported = Assert.Single(_fixture.EventsOf<SpecCompletionReported>(spec));
        Assert.Equal((spec.IntegrationBranch, tip, 0), (reported.IntegrationBranch, reported.IntegrationTip, reported.PullRequestCount));
        Assert.Equal(SpecRunStatus.Completed, spec.Status);
        Assert.NotNull(spec.CompletedAt);
        Assert.Contains((SpecRunStatus.Testing, SpecRunStatus.Completed), _fixture.Transitions(spec));
        Assert.False(_fixture.WorktreeExists(worktree));
        Assert.Empty(_fixture.Pulls.MarkedReady);
    }

    [Fact]
    public async Task Dirty_or_locked_worktrees_are_retained_with_warnings_while_the_others_are_removed()
    {
        SpecRun spec = await _fixture.TestedSpecAsync(10, 11, 12, 13);
        TicketRun dirty = _fixture.Ticket(spec, 11);
        TicketRun locked = _fixture.Ticket(spec, 12);
        _fixture.Git.SetWorktreeStatus(dirty.WorktreePath!, WorktreeStatus.Dirty);
        _fixture.Git.SetWorktreeStatus(locked.WorktreePath!, WorktreeStatus.Locked);

        await _fixture.DeliverEventsAsync();

        Assert.Equal(SpecRunStatus.AwaitingMerge, spec.Status);
        Assert.True(_fixture.WorktreeExists(dirty.WorktreePath!));
        Assert.True(_fixture.WorktreeExists(locked.WorktreePath!));
        Assert.False(_fixture.WorktreeExists(_fixture.Ticket(spec, 13).WorktreePath!));
        SpecWorktreesCleanedUp cleaned = Assert.Single(_fixture.EventsOf<SpecWorktreesCleanedUp>(spec));
        Assert.Equal(1, cleaned.Removed);
        Assert.Collection(
            cleaned.Warnings,
            warning => Assert.Contains(dirty.WorktreePath!, warning, StringComparison.Ordinal),
            warning => Assert.Contains(locked.WorktreePath!, warning, StringComparison.Ordinal));
        Assert.Equal(
            [dirty.Id, locked.Id],
            _fixture.RunEvents(spec).Where(runEvent => runEvent.Type == "WorktreeRetained").Select(runEvent => runEvent.TicketRunId!.Value));
    }

    [Fact]
    public async Task A_github_failure_while_marking_ready_keeps_the_spec_testing_and_the_next_pass_resumes_it()
    {
        SpecRun spec = await _fixture.TestedSpecAsync(10, 11, 12);
        IReadOnlyList<PullStackLayer> layers = _fixture.Layers(spec);
        _fixture.Pulls.FailMarkReadyOnce.Add(layers[1].PullRequestNumber);

        await _fixture.DeliverEventsAsync();
        CompletionResult faulted = Assert.Single(_fixture.Completions).Result;
        SpecRunStatus afterFault = spec.Status;
        MergeTrackingPass pass = await _fixture.Tracking().TrackAllAsync(Token);

        Assert.Equal(CompletionOutcome.Faulted, faulted.Outcome);
        Assert.Contains("GitHub is unavailable", faulted.Reason, StringComparison.Ordinal);
        Assert.Equal(SpecRunStatus.Testing, afterFault);
        Assert.Equal(CompletionOutcome.AwaitingMerge, pass.Completions[spec.Id].Outcome);
        Assert.Equal(SpecRunStatus.AwaitingMerge, spec.Status);
        Assert.Equal(layers.Select(layer => layer.PullRequestNumber), _fixture.Pulls.MarkedReady);
    }

    [Fact]
    public async Task A_spec_left_ready_for_review_is_finished_by_the_next_pass()
    {
        SpecRun spec = await _fixture.TestedSpecAsync(10, 11, 12);
        await _fixture.MoveAsync(spec, SpecRunStatus.ReadyForReview);
        string worktree = _fixture.Ticket(spec, 11).WorktreePath!;

        MergeTrackingPass pass = await _fixture.Tracking().TrackAllAsync(Token);

        Assert.Equal(CompletionOutcome.AwaitingMerge, pass.Completions[spec.Id].Outcome);
        Assert.Equal(SpecRunStatus.AwaitingMerge, spec.Status);
        Assert.All(_fixture.Layers(spec), layer => Assert.False(_fixture.Pulls.Snapshot(layer.PullRequestNumber).IsDraft));
        Assert.False(_fixture.WorktreeExists(worktree));
        Assert.Single(_fixture.EventsOf<SpecCompletionReported>(spec));
    }

    [Fact]
    public async Task Aborting_a_spec_removes_its_worktrees_except_one_an_agent_still_uses()
    {
        SpecRun spec = await _fixture.StartAsync(10, 11, 12);
        await _fixture.IntegrateAsync(spec, 11);
        TicketRun busy = _fixture.Ticket(spec, 12);
        busy.TransitionTo(TicketRunStatus.Ready, _fixture.Clock.UtcNow);
        busy.TransitionTo(TicketRunStatus.Implementing, _fixture.Clock.UtcNow);
        busy.WorktreePath = $"{ReadyAndMergeFixture.WorkspaceRoot}/busy";
        await _fixture.Git.PrepareWorktreeAsync(_fixture.Location, new WorktreeSpec(busy.BranchName, spec.IntegrationTipSha!.Value, busy.WorktreePath), Token);
        var step = StepRun.Create(_fixture.Ids.NewStepRunId(), spec.Id, busy.Id, StepKind.Implement, AgentRole.Implementer, 1, "hash");
        step.Start(_fixture.Clock.UtcNow, TimeSpan.FromMinutes(30));
        ((IStepRunRepository)_fixture.Store).Add(step);
        string integrated = _fixture.Ticket(spec, 11).WorktreePath!;

        await _fixture.MoveAsync(spec, SpecRunStatus.Aborted);
        await _fixture.DeliverEventsAsync();

        Assert.Equal(CompletionOutcome.CleanedUp, Assert.Single(_fixture.Completions).Result.Outcome);
        Assert.False(_fixture.WorktreeExists(integrated));
        Assert.True(_fixture.WorktreeExists(busy.WorktreePath));
        SpecWorktreesCleanedUp cleaned = Assert.Single(_fixture.EventsOf<SpecWorktreesCleanedUp>(spec));
        Assert.Equal(1, cleaned.Removed);
        Assert.Contains(busy.WorktreePath, Assert.Single(cleaned.Warnings), StringComparison.Ordinal);
        Assert.Equal(SpecRunStatus.Aborted, spec.Status);
    }

    private async Task DamageAsync(SpecRun spec, IReadOnlyList<PullStackLayer> layers, StackDamage damage)
    {
        PullRequestNumber top = layers[^1].PullRequestNumber;
        switch (damage)
        {
            case StackDamage.PullRequestHeadMoved:
                _fixture.Pulls.Tamper(top, pull => pull with { HeadSha = _fixture.Git.Commit([pull.HeadSha], "extra.cs") });
                break;
            case StackDamage.PullRequestRetargeted:
                _fixture.Pulls.Tamper(top, pull => pull with { Base = ReadyAndMergeFixture.Trunk });
                break;
            case StackDamage.PullRequestClosed:
                _fixture.Pulls.Close(top);
                break;
            case StackDamage.StackOrderChanged:
                PullStackSnapshot stack = (await _fixture.Pulls.FindStackAsync(ReadyAndMergeFixture.Repo, top, Token))!;
                _fixture.Pulls.OverrideStack(stack with { BottomToTop = [.. stack.BottomToTop.Reverse()] });
                break;
            case StackDamage.UntestedIntegrationCommit:
                await AdvanceIntegrationBranchAsync(spec);
                break;
        }
    }

    /// <summary>An integration commit that no saga published as a PR layer.</summary>
    private async Task AdvanceIntegrationBranchAsync(SpecRun spec)
    {
        CommitSha tip = spec.IntegrationTipSha!.Value;
        CommitSha extra = _fixture.Git.Commit([tip], "late.cs");
        await _fixture.Git.UpdateBranchAsync(_fixture.Location, spec.IntegrationBranch, extra, tip, Token);
        spec.IntegrationTipSha = extra;
        await _fixture.SaveAsync();
    }
}
