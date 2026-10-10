using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.ReviewLoop;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.ReviewLoop;

/// <summary>A fix turn is validated against the integration tip it was given, not one another ticket integrated meanwhile.</summary>
public sealed class FixTurnIntegrationTipTests
{
    private readonly ReviewLoopFixture _fixture = new();

    [Fact]
    public async Task integration_tip_advancing_during_the_fix_turn_does_not_fail_validation()
    {
        SeededSpec spec = await _fixture.SeedReviewingAsync(1);
        _fixture.Clean(FindingAxis.CodingStandards).Issues(FindingAxis.Specification, ReviewLoopFixture.SpecFinding);
        _fixture.Agents.Script(AgentRole.Implementer, request =>
        {
            CommitSha fixedHead = _fixture.Git.CommitInWorktree(request.Policy.Paths.WorkingDirectory, "fix.cs");
            IntegrateAnotherTicket(spec);
            return ImplementationReport.Completed(fixedHead, "Fixed the findings.");
        });
        _fixture.Clean(FindingAxis.CodingStandards).Clean(FindingAxis.Specification);

        ReviewLoopResult result = await _fixture.RunLoopAsync(spec, 1);

        Assert.Equal(ReviewLoopResult.Integrating, result);
        Assert.Equal(StepStatus.Succeeded, Assert.Single(_fixture.Steps(spec[1], StepKind.Fix)).Status);
    }

    [Fact]
    public async Task fix_turn_branch_missing_the_integration_tip_it_was_given_needs_attention()
    {
        SeededSpec spec = await _fixture.SeedReviewingAsync(1);
        IntegrateAnotherTicket(spec);
        _fixture.Clean(FindingAxis.CodingStandards).Issues(FindingAxis.Specification, ReviewLoopFixture.SpecFinding).Fix();

        ReviewLoopResult result = await _fixture.RunLoopAsync(spec, 1);

        Assert.Equal(ReviewLoopOutcome.Failed, result.Outcome);
        Assert.Contains("does not contain the integration tip", result.Reason, StringComparison.Ordinal);
        Assert.Equal(StepStatus.NeedsAttention, Assert.Single(_fixture.Steps(spec[1], StepKind.Fix)).Status);
        Assert.Equal(TicketRunStatus.NeedsAttention, _fixture.Ticket(spec[1]).Status);
        Assert.Equal(AttentionCode.TicketBranchNotBasedOnIntegration, _fixture.Ticket(spec[1]).Attention!.Code);
    }

    /// <summary>Another ticket's integration saga moves the local integration ref (the freshest tip source).</summary>
    private void IntegrateAnotherTicket(SeededSpec spec)
    {
        CommitSha tip = _fixture.Git.GetBranchTipAsync(spec.Location, spec.IntegrationBranch, GitRefScope.Local, ReviewLoopFixture.Token).GetAwaiter().GetResult()!.Value;
        CommitSha layer = _fixture.Git.Commit([tip], "other-ticket.cs");
        _fixture.Git.UpdateBranchAsync(spec.Location, spec.IntegrationBranch, layer, tip, ReviewLoopFixture.Token).GetAwaiter().GetResult();
    }
}
