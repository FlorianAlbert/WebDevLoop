using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Completion.ParentReview;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.Completion.ParentReview;

public sealed class ParentReviewTriggerTests
{
    private readonly ParentReviewFixture _fixture = new();

    private long _messageId;

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

        ParentReviewStartOutcome outcome = await _fixture.Starter().StartIfTicketsCompleteAsync(spec.Id, ParentReviewFixture.Token);

        Assert.Equal(ParentReviewStartOutcome.TicketsOutstanding, outcome);
        Assert.Equal(SpecRunStatus.Running, _fixture.Spec(spec.Id).Status);
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

        ParentReviewStartOutcome[] outcomes = await Task.WhenAll(
            _fixture.Starter().StartIfTicketsCompleteAsync(spec.Id, ParentReviewFixture.Token),
            _fixture.Starter().StartIfTicketsCompleteAsync(spec.Id, ParentReviewFixture.Token));

        Assert.Equal([ParentReviewStartOutcome.Started, ParentReviewStartOutcome.ConcurrencyConflict], outcomes.Order());
        Assert.Equal(1, _fixture.Spec(spec.Id).ReviewCycle);
    }

    [Fact]
    public async Task Spec_that_is_not_running_is_not_started()
    {
        SeededSpec spec = await _fixture.SeedParentReviewingAsync();

        ParentReviewStartOutcome outcome = await _fixture.Starter().StartIfTicketsCompleteAsync(spec.Id, ParentReviewFixture.Token);

        Assert.Equal(ParentReviewStartOutcome.NotRunning, outcome);
        Assert.Equal(1, _fixture.Spec(spec.Id).ReviewCycle);
    }
}
