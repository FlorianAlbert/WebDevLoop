using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.ReviewLoop;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.ReviewLoop;

public sealed class InterruptedFixResumeTests
{
    private readonly ReviewLoopFixture _fixture = new();

    [Fact]
    public async Task a_ticket_left_fixing_without_an_active_step_resumes_the_fix_without_using_another_review_iteration()
    {
        SeededSpec spec = await _fixture.SeedReviewingAsync(1);
        StepRun interrupted = await InterruptFixAsync(spec);
        _fixture.Fix().Clean(FindingAxis.CodingStandards).Clean(FindingAxis.Specification);

        ReviewLoopResult result = await _fixture.Loop().RunAsync(
            new ReviewAssignment(spec.Id, spec[1], new AgentSessionId(interrupted.CopilotSessionId!)), ReviewLoopFixture.Token);

        Assert.Equal(ReviewLoopResult.Integrating, result);
        Assert.Equal(interrupted.CopilotSessionId, _fixture.Agents.Resumed[^1].SessionId.Value);
        Assert.Contains("cs-1", _fixture.Agents.Resumed[^1].Prompt);
        TicketRun ticket = _fixture.Ticket(spec[1]);
        Assert.Equal((TicketRunStatus.Integrating, 1), (ticket.Status, ticket.ReviewIteration));
        Assert.Equal([StepStatus.Failed, StepStatus.Succeeded], _fixture.Steps(spec[1], StepKind.Fix).Select(step => step.Status));
        Assert.Equal(4, _fixture.Steps(spec[1], StepKind.Review).Count(step => step.Status == StepStatus.Succeeded));
    }

    [Fact]
    public async Task a_fixing_ticket_with_a_running_fix_step_is_already_running()
    {
        SeededSpec spec = await _fixture.SeedReviewingAsync(1);
        await InterruptFixAsync(spec, finish: false);

        ReviewLoopResult result = await _fixture.RunLoopAsync(spec, 1);

        Assert.Equal(ReviewLoopResult.AlreadyRunning, result);
    }

    /// <summary>Runs the loop until the fix turn is cut off like a shutdown; recovery then finishes the step as interrupted.</summary>
    private async Task<StepRun> InterruptFixAsync(SeededSpec spec, bool finish = true)
    {
        _fixture.UseTemplate(AgentRole.Implementer, "{review_findings_json}");
        _fixture.Issues(FindingAxis.CodingStandards, ReviewLoopFixture.StandardsFinding).Clean(FindingAxis.Specification);
        _fixture.Agents.Script(AgentRole.Implementer, _ => throw new OperationCanceledException("Shutting down."));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _fixture.RunLoopAsync(spec, 1));
        Assert.Equal(TicketRunStatus.FixingReviewFindings, _fixture.Ticket(spec[1]).Status);

        CasWorkflowScope scope = _fixture.Db.OpenScope();
        StepRun fix = Assert.Single(await scope.ListByTicketRunAsync(spec[1], ReviewLoopFixture.Token), step => step.Kind == StepKind.Fix);
        Assert.Equal(StepStatus.Running, fix.Status);
        if (finish)
        {
            fix.Finish(StepStatus.Failed, _fixture.Execution.Clock.UtcNow, failureReason: "Interrupted by a restart.");
            Assert.Equal(SaveOutcome.Saved, await scope.SaveChangesAsync(ReviewLoopFixture.Token));
        }

        return fix;
    }
}
