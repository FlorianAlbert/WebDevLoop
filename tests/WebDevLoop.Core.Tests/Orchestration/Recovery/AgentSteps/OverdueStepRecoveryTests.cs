using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Recovery.AgentSteps;
using WebDevLoop.Core.Orchestration.ReviewLoop;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.Recovery.AgentSteps;

/// <summary>
/// A runner enforces its step's timeout itself, so a step of this process still running well past it has lost its runner
/// (or the boot-time check missed a previous process's step, e.g. after the clock moved back).
/// </summary>
public sealed class OverdueStepRecoveryTests
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromMinutes(30);

    private readonly AgentStepRecoveryFixture _fixture = new();

    [Fact]
    public async Task a_step_running_past_its_timeout_and_the_grace_period_is_aborted_timed_out_and_relaunched()
    {
        SeededSpec spec = await SeedReviewingTicketAsync();
        StepRun review = await _fixture.SeedRunningStepAsync(spec.Id, spec[1], StepKind.Review, AgentRole.ReviewerCodingStandards, "review-cs");
        _fixture.Clock.Advance(StepTimeout + AgentStepRecoveryFixture.GracePeriod);

        AgentStepRecoveryReport report = await _fixture.RecoverAsync();

        StepRun overdue = _fixture.Step("review-cs");
        Assert.Equal((StepStatus.TimedOut, StepInterruption.Overdue(review.TimeoutAt!.Value)), (overdue.Status, overdue.FailureReason));
        Assert.Equal([new AgentSessionId("session-review-cs")], _fixture.Review.Agents.Aborted);
        Assert.Equal([review.Id], report.InterruptedSteps);
        Assert.Equal([new ReviewAssignment(spec.Id, spec[1])], _fixture.Review.Launcher.Launched);
    }

    [Fact]
    public async Task a_step_within_its_timeout_and_the_grace_period_is_left_to_its_runner()
    {
        SeededSpec spec = await SeedReviewingTicketAsync();
        await _fixture.SeedRunningStepAsync(spec.Id, spec[1], StepKind.Review, AgentRole.ReviewerCodingStandards, "review-cs");
        _fixture.Clock.Advance(StepTimeout + AgentStepRecoveryFixture.GracePeriod - TimeSpan.FromSeconds(1));

        AgentStepRecoveryReport report = await _fixture.RecoverAsync();

        Assert.Equal(StepStatus.Running, _fixture.Step("review-cs").Status);
        Assert.Empty(_fixture.Review.Agents.Aborted);
        Assert.Empty(report.Relaunched);
    }

    [Fact]
    public async Task an_overdue_test_step_is_left_to_its_lease()
    {
        SeededSpec spec = await _fixture.Testing.SeedTestingAsync();
        await _fixture.SeedRunningStepAsync(spec.Id, null, StepKind.Test, AgentRole.Tester, "test-1");
        _fixture.SeedLease(spec.Id, 41003, TimeSpan.FromHours(2));
        _fixture.Clock.Advance(TimeSpan.FromHours(1));

        AgentStepRecoveryReport report = await _fixture.RecoverAsync();

        Assert.Equal(StepStatus.Running, _fixture.Step("test-1").Status);
        Assert.Empty(report.InterruptedSteps);
    }

    private async Task<SeededSpec> SeedReviewingTicketAsync()
    {
        SeededSpec spec = await _fixture.Execution.SeedRunningSpecAsync("app", (1, []));
        await _fixture.Execution.MoveAsync(spec[1], TicketRunStatus.Ready, TicketRunStatus.Implementing, TicketRunStatus.Reviewing);
        return spec;
    }
}
