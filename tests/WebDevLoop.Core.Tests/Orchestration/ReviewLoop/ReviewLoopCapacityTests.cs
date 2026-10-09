using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.ReviewLoop;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.ReviewLoop;

/// <summary>Implementer slot accounting of fix turns inside a long-lived review loop scope, and waking waiting loops.</summary>
public sealed class ReviewLoopCapacityTests
{
    private readonly ReviewLoopFixture _fixture = new();

    [Fact]
    public async Task fix_turn_sees_a_slot_freed_after_the_loop_loaded_its_sibling_tickets()
    {
        SeededSpec spec = await _fixture.SeedReviewingAsync(1, 2);
        _fixture.Settings.Configure(spec.RepositoryId, settings => settings with { MaxConcurrentImplementersPerRepo = 1 });
        await _fixture.Execution.MoveAsync(spec[2], TicketRunStatus.FixingReviewFindings);
        _fixture.Clean(FindingAxis.CodingStandards).Script(FindingAxis.Specification, _ =>
            {
                // The loop already loaded ticket 2 (as FixingReviewFindings) for the reviewer prompt; its fix finishes now.
                _fixture.Execution.MoveAsync(spec[2], TicketRunStatus.Reviewing).GetAwaiter().GetResult();
                return ReviewReport.IssuesFound(FindingAxis.Specification, "Specification found issues.", [ReviewLoopFixture.SpecFinding]);
            })
            .Fix()
            .Clean(FindingAxis.CodingStandards).Clean(FindingAxis.Specification);

        ReviewLoopResult result = await _fixture.RunLoopAsync(spec, 1);

        Assert.Equal(ReviewLoopResult.Integrating, result);
        Assert.Single(_fixture.Agents.Resumed);
    }

    [Fact]
    public async Task reconciliation_relaunches_a_loop_whose_findings_wait_for_an_implementer_slot()
    {
        SeededSpec spec = await _fixture.SeedReviewingAsync(1, 2);
        _fixture.Settings.Configure(spec.RepositoryId, settings => settings with { MaxConcurrentImplementersPerRepo = 1 });
        await _fixture.Execution.MoveAsync(spec[2], TicketRunStatus.FixingReviewFindings);
        _fixture.Clean(FindingAxis.CodingStandards).Issues(FindingAxis.Specification, ReviewLoopFixture.SpecFinding);
        Assert.Equal(ReviewLoopResult.AwaitingImplementerCapacity, await _fixture.RunLoopAsync(spec, 1));
        _fixture.Db.TakeUndispatchedEvents();

        await _fixture.Handler().HandleAsync(new EventEnvelope(1, new FrontierReconciliationRequested(spec.Id, TicketExecutionFixture.T0)), ReviewLoopFixture.Token);

        Assert.Equal([new ReviewAssignment(spec.Id, spec[1])], _fixture.Launcher.Launched);
    }
}
