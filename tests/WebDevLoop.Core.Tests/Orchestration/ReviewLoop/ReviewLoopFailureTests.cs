using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.ReviewLoop;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.ReviewLoop;

/// <summary>
/// Unexpected exceptions inside review rounds and fix turns must not leave steps running (which blocks every relaunch), and
/// reconciliation relaunches review loops that died between rounds.
/// </summary>
public sealed class ReviewLoopFailureTests
{
    private readonly ReviewLoopFixture _fixture = new();

    [Fact]
    public async Task reviewer_exception_finishes_its_step_as_failed_and_the_axis_is_retried()
    {
        SeededSpec spec = await _fixture.SeedReviewingAsync(1);
        _fixture.Clean(FindingAxis.CodingStandards)
            .Script(FindingAxis.Specification, _ => throw new HttpRequestException("Copilot runtime unavailable."))
            .Clean(FindingAxis.Specification);

        ReviewLoopResult result = await _fixture.RunLoopAsync(spec, 1);

        Assert.Equal(ReviewLoopResult.Integrating, result);
        IReadOnlyList<StepRun> reviews = _fixture.Steps(spec[1], StepKind.Review);
        Assert.Equal(
            [
                (AgentRole.ReviewerCodingStandards, StepStatus.Succeeded),
                (AgentRole.ReviewerSpecification, StepStatus.Failed),
                (AgentRole.ReviewerSpecification, StepStatus.Succeeded),
            ],
            reviews.Select(step => (step.AgentRole!.Value, step.Status)));
        Assert.Contains("Copilot runtime unavailable.", reviews[1].FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task fix_turn_exception_finishes_its_step_as_failed_and_is_retried_in_a_fresh_session()
    {
        SeededSpec spec = await _fixture.SeedReviewingAsync(1);
        _fixture.Clean(FindingAxis.CodingStandards).Issues(FindingAxis.Specification, ReviewLoopFixture.SpecFinding);
        _fixture.Agents.Script(AgentRole.Implementer, _ => throw new HttpRequestException("Copilot runtime unavailable."));
        _fixture.Fix().Clean(FindingAxis.CodingStandards).Clean(FindingAxis.Specification);

        ReviewLoopResult result = await _fixture.RunLoopAsync(spec, 1);

        Assert.Equal(ReviewLoopResult.Integrating, result);
        IReadOnlyList<StepRun> fixes = _fixture.Steps(spec[1], StepKind.Fix);
        Assert.Equal([StepStatus.Failed, StepStatus.Succeeded], fixes.Select(step => step.Status));
        Assert.Contains("Copilot runtime unavailable.", fixes[0].FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task fix_turn_exceptions_on_every_attempt_free_the_implementer_slot_through_needs_attention()
    {
        SeededSpec spec = await _fixture.SeedReviewingAsync(1);
        _fixture.Settings.Configure(spec.RepositoryId, settings => settings with { MaxRetries = 1 });
        _fixture.Clean(FindingAxis.CodingStandards).Issues(FindingAxis.Specification, ReviewLoopFixture.SpecFinding);
        _fixture.Agents.Script(AgentRole.Implementer, _ => throw new HttpRequestException("Copilot runtime unavailable."));
        _fixture.Agents.Script(AgentRole.Implementer, _ => throw new HttpRequestException("Copilot runtime unavailable."));

        ReviewLoopResult result = await _fixture.RunLoopAsync(spec, 1);

        Assert.Equal(ReviewLoopOutcome.Failed, result.Outcome);
        Assert.Equal(TicketRunStatus.NeedsAttention, _fixture.Ticket(spec[1]).Status);
        Assert.Equal([StepStatus.Failed, StepStatus.Failed], _fixture.Steps(spec[1], StepKind.Fix).Select(step => step.Status));
    }

    [Fact]
    public async Task reconciliation_relaunches_reviewing_tickets_without_an_active_step()
    {
        SeededSpec spec = await _fixture.SeedReviewingAsync(1, 2);
        _fixture.Db.TakeUndispatchedEvents();
        await StartReviewStepAsync(spec, 2);

        await _fixture.Handler().HandleAsync(new EventEnvelope(1, new FrontierReconciliationRequested(spec.Id, TicketExecutionFixture.T0)), ReviewLoopFixture.Token);

        Assert.Equal([new ReviewAssignment(spec.Id, spec[1])], _fixture.Launcher.Launched);
    }

    /// <summary>A review turn of another (still running) loop.</summary>
    private async Task StartReviewStepAsync(SeededSpec spec, int ticketNumber)
    {
        CasWorkflowScope scope = _fixture.Db.OpenScope();
        StepRun step = StepRun.Create(new StepRunId($"running-review-{ticketNumber}"), spec.Id, spec[ticketNumber], StepKind.Review, AgentRole.ReviewerSpecification, 1, "hash");
        step.Start(TicketExecutionFixture.T0, TimeSpan.FromMinutes(30));
        scope.Add(step);
        Assert.Equal(Core.Ports.SaveOutcome.Saved, await scope.SaveChangesAsync(ReviewLoopFixture.Token));
    }
}
