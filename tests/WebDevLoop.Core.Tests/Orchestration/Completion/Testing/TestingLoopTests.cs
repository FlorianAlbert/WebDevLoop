using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Completion.ParentReview;
using WebDevLoop.Core.Orchestration.Completion.Testing;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.Completion.Testing;

/// <summary>Steps 9–12 end to end: test issues become a frontier, then parent review and testing repeat until the tester passes.</summary>
public sealed class TestingLoopTests
{
    private readonly TestingFixture _fixture = new();

    [Fact]
    public async Task Test_finding_tickets_are_worked_then_parent_review_and_testing_repeat_until_the_tester_passes()
    {
        SeededSpec spec = await _fixture.Parent.SeedParentReviewingAsync();
        _fixture.Parent.Clean();
        Assert.Equal(ParentReviewOutcome.ReadyForTesting, (await _fixture.Parent.RunAsync(spec.Id)).Outcome);
        await _fixture.PumpEventsAsync();
        Assert.Equal([new TestingAssignment(spec.Id)], _fixture.Launcher.Launched);

        _fixture.Tester.Reports(TestingFixture.IssuesFound(TestingFixture.EmptyTitleCrash));
        TestingResult first = await _fixture.RunAsync(spec.Id);
        Assert.Equal(TestingOutcome.FindingTicketsCreated, first.Outcome);
        TicketRunId finding = Assert.Single(first.Tickets).TicketRunId;
        await _fixture.PumpEventsAsync();

        Assert.Equal([new ImplementationAssignment(spec.Id, finding)], _fixture.Execution.Launcher.Launched);
        Assert.Equal(SpecRunStatus.Running, _fixture.Spec(spec.Id).Status);
        Assert.Empty(_fixture.Parent.Launcher.Launched);

        await _fixture.Execution.IntegrateAsync(spec, finding);
        await _fixture.PumpEventsAsync();
        SpecRun reviewing = _fixture.Spec(spec.Id);
        Assert.Equal((SpecRunStatus.ParentReviewing, 2, 1), (reviewing.Status, reviewing.ReviewCycle, reviewing.TestCycle));
        Assert.Equal([new ParentReviewAssignment(spec.Id)], _fixture.Parent.Launcher.Launched);

        _fixture.Parent.Clean();
        Assert.Equal(ParentReviewOutcome.ReadyForTesting, (await _fixture.Parent.RunAsync(spec.Id)).Outcome);
        await _fixture.PumpEventsAsync();
        Assert.Equal(2, _fixture.Launcher.Launched.Count);

        _fixture.Tester.Reports(TestingFixture.Pass());
        TestingResult second = await _fixture.RunAsync(spec.Id);

        Assert.Equal(TestingOutcome.Passed, second.Outcome);
        SpecRun passed = _fixture.Spec(spec.Id);
        Assert.Equal((SpecRunStatus.Testing, 2), (passed.Status, passed.TestCycle));
        Assert.Equal(2, Assert.Single(_fixture.Events.OfType<SpecTestingPassed>()).TestCycle);
        Assert.Single(_fixture.Issues.CreatedDrafts);
        Assert.Equal(2, _fixture.Tester.Started.Count);
        Assert.Equal([1, 2], _fixture.TestSteps(spec.Id).Select(step => step.Attempt));
    }
}
