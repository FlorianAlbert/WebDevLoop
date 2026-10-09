using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.ReviewLoop;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.ReviewLoop;

public sealed class ReviewLoopEventHandlerTests
{
    private readonly ReviewLoopFixture _fixture = new();

    [Fact]
    public async Task implemented_ticket_entering_review_launches_its_review_loop_once()
    {
        SeededSpec spec = await _fixture.SeedReviewingAsync(1, 2);

        await _fixture.DeliverEventsAsync();

        Assert.Equal(
            [new ReviewAssignment(spec.Id, spec[1]), new ReviewAssignment(spec.Id, spec[2])],
            _fixture.Launcher.Launched.OrderBy(assignment => assignment.TicketRunId.Value));
    }

    [Fact]
    public async Task ticket_returning_from_a_fix_is_not_relaunched_by_the_event()
    {
        SeededSpec spec = await _fixture.SeedReviewingAsync(1);
        _fixture.Db.TakeUndispatchedEvents();
        await _fixture.Execution.MoveAsync(spec[1], TicketRunStatus.FixingReviewFindings, TicketRunStatus.Reviewing);

        await _fixture.DeliverEventsAsync();

        Assert.Empty(_fixture.Launcher.Launched);
    }

    [Fact]
    public async Task a_ticket_retried_into_review_launches_its_review_loop()
    {
        SeededSpec spec = await _fixture.SeedReviewingAsync(1);
        await _fixture.Execution.MoveAsync(spec[1], TicketRunStatus.NeedsAttention);
        _fixture.Db.TakeUndispatchedEvents();
        await _fixture.Execution.MoveAsync(spec[1], TicketRunStatus.Reviewing);

        await _fixture.DeliverEventsAsync();

        Assert.Equal([new ReviewAssignment(spec.Id, spec[1])], _fixture.Launcher.Launched);
    }
}
