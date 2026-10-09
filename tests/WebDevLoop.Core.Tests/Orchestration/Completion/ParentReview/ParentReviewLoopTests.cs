using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Completion.ParentReview;
using WebDevLoop.Core.Orchestration.Findings;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Tests.Orchestration.Findings;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.Completion.ParentReview;

/// <summary>Steps 9–10 end to end: findings become a frontier that the normal ticket flow works, then the review repeats.</summary>
public sealed class ParentReviewLoopTests
{
    private readonly ParentReviewFixture _fixture = new();

    [Fact]
    public async Task Finding_tickets_are_worked_through_the_frontier_and_the_parent_review_repeats_until_clean()
    {
        SeededSpec spec = await _fixture.SeedParentReviewingAsync();
        _fixture.SpecificationIssues(
            FindingsFixture.Missing("Errors are not logged.", "src/Feature.cs", 10),
            FindingsFixture.Missing("Retries are missing.", "src/Feature.cs", 40));

        ParentReviewResult first = await _fixture.RunAsync(spec.Id);
        Assert.Equal(ParentReviewOutcome.FindingTicketsCreated, first.Outcome);
        (TicketRunId logging, TicketRunId retries) = (first.Tickets[0].TicketRunId, first.Tickets[1].TicketRunId);
        await _fixture.PumpEventsAsync();

        Assert.Equal([new ImplementationAssignment(spec.Id, logging)], _fixture.Execution.Launcher.Launched);
        Assert.Equal(TicketRunStatus.Blocked, _fixture.Execution.Ticket(retries).Status);
        Assert.Equal(SpecRunStatus.Running, _fixture.Spec(spec.Id).Status);

        await _fixture.Execution.IntegrateAsync(spec, logging);
        await _fixture.PumpEventsAsync();
        Assert.Equal(new ImplementationAssignment(spec.Id, retries), _fixture.Execution.Launcher.Launched[^1]);
        Assert.Empty(_fixture.Launcher.Launched);

        await _fixture.Execution.IntegrateAsync(spec, retries);
        await _fixture.PumpEventsAsync();

        SpecRun reviewing = _fixture.Spec(spec.Id);
        Assert.Equal((SpecRunStatus.ParentReviewing, 2), (reviewing.Status, reviewing.ReviewCycle));
        Assert.Equal([new ParentReviewAssignment(spec.Id)], _fixture.Launcher.Launched);

        _fixture.Clean();
        ParentReviewResult second = await _fixture.RunAsync(spec.Id);

        Assert.Equal(ParentReviewOutcome.ReadyForTesting, second.Outcome);
        Assert.Equal(SpecRunStatus.Testing, _fixture.Spec(spec.Id).Status);
        Assert.Equal(4, _fixture.ReviewerRequests.Count());
        Assert.Equal(2, _fixture.Issues.CreatedDrafts.Count);
        Assert.All(first.Tickets, ticket => Assert.Equal(FindingTicketOrigin.NewTicket, ticket.Origin));
    }
}
