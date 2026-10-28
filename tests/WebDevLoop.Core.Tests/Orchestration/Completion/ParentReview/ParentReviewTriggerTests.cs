using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Completion;
using WebDevLoop.Core.Orchestration.Completion.ParentReview;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.Completion.ParentReview;

public sealed class ParentReviewTriggerTests
{
    private readonly ParentReviewFixture _fixture = new();

    private long _messageId;

    [Fact]
    public async Task independent_completion_decisions_read_fresh_permission_instead_of_reusing_previous_eligibility()
    {
        SeededSpec spec = await _fixture.SeedIntegratedSpecAsync();
        var issues = _fixture.Issues.Snapshots;
        IssueRef remaining = new("octo", "app", 3);
        issues.Seed(remaining, "New work", _fixture.Spec(spec.Id).ParentIssue);
        WorkCompletionDecider completion = _fixture.Completion();

        WorkCompletionResult outstanding = await completion.DecideCompletionAsync(spec.Id, ParentReviewFixture.Token);

        Assert.Equal(WorkCompletionOutcome.TicketsOutstanding, outstanding.Outcome);
        IssueSnapshot child = await issues.GetIssueAsync(remaining, ParentReviewFixture.Token);
        issues.Seed(child with { Labels = [] });

        WorkCompletionResult ready = await completion.DecideCompletionAsync(spec.Id, ParentReviewFixture.Token);

        Assert.Equal(WorkCompletionOutcome.ReviewStarted, ready.Outcome);
        Assert.Equal(SpecRunStatus.ParentReviewing, _fixture.Spec(spec.Id).Status);
        Assert.Equal(1, _fixture.Spec(spec.Id).ReviewCycle);
    }

    [Theory]
    [InlineData(SpecRunStatus.Aborted)]
    [InlineData(SpecRunStatus.NeedsAttention)]
    [InlineData(SpecRunStatus.ParentReviewing)]
    public async Task leaving_work_selection_clears_the_previous_work_decision_reason(SpecRunStatus next)
    {
        SeededSpec spec = await _fixture.Execution.SeedRunningSpecAsync("app", (2, []));
        var blocker = new IssueRef("another-owner", "library", 900);
        _fixture.Issues.Snapshots.SetBlockedBy(_fixture.Execution.Ticket(spec[2]).Issue, blocker);
        await _fixture.Completion().DecideCompletionAsync(spec.Id, ParentReviewFixture.Token);
        Assert.NotNull(_fixture.Spec(spec.Id).WorkDecisionReason);

        await _fixture.MoveSpecAsync(spec.Id, next);

        Assert.Null(_fixture.Spec(spec.Id).WorkDecisionReason);
        Assert.Equal(next, _fixture.Spec(spec.Id).Status);
    }

    [Fact]
    public async Task no_eligible_work_finishes_even_if_a_new_parent_blocker_is_open()
    {
        SeededSpec spec = await _fixture.Execution.SeedRunningSpecAsync("app", (2, []));
        var issues = _fixture.Issues.Snapshots;
        var blocker = new IssueRef("another-owner", "library", 900);
        issues.Seed(blocker, "New parent blocker");
        issues.SetBlockedBy(_fixture.Spec(spec.Id).ParentIssue, blocker);
        var child = await issues.GetIssueAsync(_fixture.Execution.Ticket(spec[2]).Issue, ParentReviewFixture.Token);
        issues.Seed(child with { Labels = [] });

        WorkCompletionResult result = await _fixture.Completion().DecideCompletionAsync(spec.Id, ParentReviewFixture.Token);

        Assert.Equal(WorkCompletionOutcome.NoEligibleWork, result.Outcome);
        Assert.Equal(SpecRunStatus.Completed, _fixture.Spec(spec.Id).Status);
        Assert.Equal(0, _fixture.Spec(spec.Id).ReviewCycle);
    }

    [Fact]
    public async Task newly_opted_in_sub_issue_prevents_review_before_local_graph_reconciliation()
    {
        SeededSpec spec = await _fixture.SeedIntegratedSpecAsync();
        _fixture.Issues.Snapshots.Seed(new IssueRef("octo", "app", 3), "New opted-in work", _fixture.Spec(spec.Id).ParentIssue);

        WorkCompletionResult result = await _fixture.Completion().DecideCompletionAsync(spec.Id, ParentReviewFixture.Token);

        Assert.Equal(WorkCompletionOutcome.TicketsOutstanding, result.Outcome);
        Assert.Equal(SpecRunStatus.Running, _fixture.Spec(spec.Id).Status);
        Assert.Equal(0, _fixture.Spec(spec.Id).ReviewCycle);
    }

    [Fact]
    public async Task integrated_work_with_remaining_eligible_blocked_tickets_waits_without_parent_review()
    {
        SeededSpec spec = await _fixture.Execution.SeedRunningSpecAsync("app", (2, []), (3, []));
        await _fixture.WorkTicketAsync(spec, spec[2]);
        var blocker = new IssueRef("another-owner", "library", 900);
        _fixture.Issues.Snapshots.Seed(blocker, "External blocker");
        _fixture.Issues.Snapshots.SetBlockedBy(_fixture.Execution.Ticket(spec[3]).Issue, blocker);

        WorkCompletionResult result = await _fixture.Completion().DecideCompletionAsync(spec.Id, ParentReviewFixture.Token);

        Assert.Equal(WorkCompletionOutcome.Parked, result.Outcome);
        Assert.Equal(SpecRunStatus.WaitingForTickets, _fixture.Spec(spec.Id).Status);
        Assert.Contains("open native blocker", _fixture.Spec(spec.Id).WorkDecisionReason);
        Assert.Null(_fixture.Spec(spec.Id).MaxActiveSpecsSlot);
        Assert.Equal(0, _fixture.Spec(spec.Id).ReviewCycle);
    }

    private Task DeliverAsync(WorkflowEvent workflowEvent) =>
        _fixture.Handler().HandleAsync(new EventEnvelope(++_messageId, workflowEvent), ParentReviewFixture.Token);

    private async Task DeliverCommittedAsync()
    {
        foreach (WorkflowEvent workflowEvent in _fixture.Db.TakeUndispatchedEvents())
        {
            await DeliverAsync(workflowEvent);
        }
    }

    [Fact]
    public async Task A_label_lookup_failure_defers_parent_review_with_diagnostics_until_a_successful_retry()
    {
        SeededSpec spec = await _fixture.SeedIntegratedSpecAsync();
        _fixture.Execution.Labels.LookupFailures[new GitHubRepoRef("octo", "app")] =
            new RepositoryLabelException("Check Issues permissions and retry.");

        WorkCompletionResult deferred = await _fixture.Completion().DecideCompletionAsync(spec.Id, ParentReviewFixture.Token);

        Assert.Equal(WorkCompletionOutcome.GitHubUnavailable, deferred.Outcome);
        Assert.Contains("Check Issues permissions and retry.", Assert.IsType<GitHubIssuesException>(deferred.LookupError).Message);
        Assert.Equal(SpecRunStatus.Running, _fixture.Spec(spec.Id).Status);
        Assert.Empty(_fixture.Launcher.Launched);
        _fixture.Execution.Labels.LookupFailures.Clear();

        WorkCompletionResult recovered = await _fixture.Completion().DecideCompletionAsync(spec.Id, ParentReviewFixture.Token);

        Assert.Equal(WorkCompletionOutcome.ReviewStarted, recovered.Outcome);
        Assert.Null(recovered.LookupError);
        Assert.Equal(SpecRunStatus.ParentReviewing, _fixture.Spec(spec.Id).Status);
    }

    [Fact]
    public async Task A_deferred_parent_review_reports_label_errors_through_the_event_bus_boundary()
    {
        SeededSpec spec = await _fixture.SeedIntegratedSpecAsync();
        _fixture.Execution.Labels.LookupFailures[new GitHubRepoRef("octo", "app")] =
            new RepositoryLabelException("Check Issues permissions and retry.");

        GitHubIssuesException error = await Assert.ThrowsAsync<GitHubIssuesException>(() =>
            DeliverAsync(new FrontierReconciliationRequested(spec.Id, TicketExecutionFixture.T0)));

        Assert.Contains("Check Issues permissions and retry.", error.Message);
        Assert.Equal(SpecRunStatus.Running, _fixture.Spec(spec.Id).Status);
        Assert.Empty(_fixture.Launcher.Launched);
    }

    [Fact]
    public async Task Integrating_the_last_ticket_starts_the_parent_review_and_launches_it()
    {
        SeededSpec spec = await _fixture.Execution.SeedRunningSpecAsync("app", (2, []), (3, []));
        await _fixture.WorkTicketAsync(spec, spec[2]);
        await DeliverCommittedAsync();
        Assert.Equal(SpecRunStatus.Running, _fixture.Spec(spec.Id).Status);

        await _fixture.WorkTicketAsync(spec, spec[3]);
        await DeliverCommittedAsync();
        await DeliverCommittedAsync();

        SpecRun reviewing = _fixture.Spec(spec.Id);
        Assert.Equal((SpecRunStatus.ParentReviewing, 1), (reviewing.Status, reviewing.ReviewCycle));
        SpecRunStatusChanged changed = Assert.Single(_fixture.Db.CommittedEvents.OfType<SpecRunStatusChanged>());
        Assert.Equal((spec.RepositoryId, SpecRunStatus.Running, SpecRunStatus.ParentReviewing), (changed.RepositoryId, changed.From, changed.To));
        Assert.Equal([new ParentReviewAssignment(spec.Id)], _fixture.Launcher.Launched);
    }

    [Fact]
    public async Task Outstanding_tickets_keep_the_spec_running()
    {
        SeededSpec spec = await _fixture.Execution.SeedRunningSpecAsync("app", (2, []), (3, [2]));
        await _fixture.WorkTicketAsync(spec, spec[2]);

        WorkCompletionResult result = await _fixture.Completion().DecideCompletionAsync(spec.Id, ParentReviewFixture.Token);

        Assert.Equal(WorkCompletionOutcome.TicketsOutstanding, result.Outcome);
        Assert.Equal(SpecRunStatus.Running, _fixture.Spec(spec.Id).Status);
        await DeliverCommittedAsync();
        Assert.Empty(_fixture.Launcher.Launched);
    }

    [Fact]
    public async Task A_spec_without_any_ticket_finishes_without_parent_review()
    {
        SeededSpec spec = await _fixture.Execution.SeedRunningSpecAsync("app");

        WorkCompletionResult result = await _fixture.Completion().DecideCompletionAsync(spec.Id, ParentReviewFixture.Token);

        Assert.Equal(WorkCompletionOutcome.NoEligibleWork, result.Outcome);
        Assert.Equal(SpecRunStatus.Completed, _fixture.Spec(spec.Id).Status);
        Assert.Contains("No eligible work", _fixture.Spec(spec.Id).WorkDecisionReason);
        await DeliverCommittedAsync();
        Assert.Empty(_fixture.Launcher.Launched);
    }

    [Fact]
    public async Task Skipped_tickets_count_as_complete()
    {
        SeededSpec spec = await _fixture.Execution.SeedRunningSpecAsync("app", (2, []), (3, []));
        await _fixture.WorkTicketAsync(spec, spec[2]);
        await _fixture.Execution.MoveAsync(spec[3], TicketRunStatus.Skipped);

        await DeliverCommittedAsync();

        Assert.Equal(SpecRunStatus.ParentReviewing, _fixture.Spec(spec.Id).Status);
    }

    [Fact]
    public async Task Aborted_tickets_count_as_complete()
    {
        SeededSpec spec = await _fixture.Execution.SeedRunningSpecAsync("app", (2, []), (3, []));
        await _fixture.WorkTicketAsync(spec, spec[2]);
        await _fixture.Execution.MoveAsync(spec[3], TicketRunStatus.Aborted);

        await DeliverCommittedAsync();

        Assert.Equal(SpecRunStatus.ParentReviewing, _fixture.Spec(spec.Id).Status);
    }

    [Fact]
    public async Task Spec_returning_to_running_with_all_tickets_done_starts_the_next_parent_review_cycle()
    {
        SeededSpec spec = await _fixture.SeedParentReviewingAsync();
        await _fixture.MoveSpecAsync(spec.Id, SpecRunStatus.NeedsAttention);
        await _fixture.MoveSpecAsync(spec.Id, SpecRunStatus.Running);

        await DeliverCommittedAsync();

        SpecRun reviewing = _fixture.Spec(spec.Id);
        Assert.Equal((SpecRunStatus.ParentReviewing, 2), (reviewing.Status, reviewing.ReviewCycle));
    }

    [Fact]
    public async Task Concurrent_starts_move_the_spec_into_parent_review_once()
    {
        SeededSpec spec = await _fixture.SeedIntegratedSpecAsync();
        _fixture.Db.HoldSavesUntil(2);

        WorkCompletionResult[] results = await Task.WhenAll(
            _fixture.Completion().DecideCompletionAsync(spec.Id, ParentReviewFixture.Token),
            _fixture.Completion().DecideCompletionAsync(spec.Id, ParentReviewFixture.Token));

        Assert.Equal([WorkCompletionOutcome.ReviewStarted, WorkCompletionOutcome.ConcurrencyConflict], results.Select(result => result.Outcome).Order());
        Assert.Equal(1, _fixture.Spec(spec.Id).ReviewCycle);
    }

    [Fact]
    public async Task Spec_that_is_not_running_is_not_started()
    {
        SeededSpec spec = await _fixture.SeedParentReviewingAsync();

        WorkCompletionResult result = await _fixture.Completion().DecideCompletionAsync(spec.Id, ParentReviewFixture.Token);

        Assert.Equal(WorkCompletionOutcome.NotRunning, result.Outcome);
        Assert.Equal(1, _fixture.Spec(spec.Id).ReviewCycle);
    }
}
