using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.ReviewLoop;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.ReviewLoop;

public sealed class FixSessionFallbackTests
{
    private readonly ReviewLoopFixture _fixture = new();

    [Fact]
    public async Task a_missing_implementer_session_restarts_the_fix_turn_in_a_fresh_session_without_using_a_retry()
    {
        SeededSpec spec = await _fixture.SeedReviewingAsync(1);
        _fixture.Settings.Configure(spec.RepositoryId, settings => settings with { MaxRetries = 0 });
        StepRun implement = Assert.Single(_fixture.Steps(spec[1], StepKind.Implement));
        var original = new AgentSessionId(implement.CopilotSessionId!);
        _fixture.Agents.MissingSessions.Add(original);
        _fixture.Issues(FindingAxis.CodingStandards, ReviewLoopFixture.StandardsFinding).Clean(FindingAxis.Specification)
            .Fix()
            .Clean(FindingAxis.CodingStandards).Clean(FindingAxis.Specification);

        ReviewLoopResult result = await _fixture.RunLoopAsync(spec, 1);

        Assert.Equal(ReviewLoopResult.Integrating, result);
        Assert.Equal(original, Assert.Single(_fixture.Agents.Resumed).SessionId);
        AgentRunRequest fresh = Assert.Single(_fixture.Agents.Started, request => request.Role == AgentRole.Implementer);
        Assert.NotEqual(original, fresh.SessionId);
        StepRun fix = Assert.Single(_fixture.Steps(spec[1], StepKind.Fix));
        Assert.Equal(StepStatus.Succeeded, fix.Status);
        Assert.Equal(fresh.SessionId.Value, fix.CopilotSessionId);
        Assert.Equal(fix.Id, fresh.StepRunId);
    }
}
