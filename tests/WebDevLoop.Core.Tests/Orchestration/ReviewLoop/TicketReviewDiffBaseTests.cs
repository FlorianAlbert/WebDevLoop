using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.ReviewLoop;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.ReviewLoop;

/// <summary>A ticket review diffs against the integration commit last merged into the ticket, however far the tip has moved since.</summary>
public sealed class TicketReviewDiffBaseTests
{
    private const string DiffTemplate = "{diff_base_ref}|{diff_head_ref}|{changed_files}";

    private readonly ReviewLoopFixture _fixture = new();

    [Fact]
    public async Task review_after_the_integration_tip_moved_past_the_last_merge_sees_only_the_tickets_own_changes()
    {
        _fixture.UseTemplate(AgentRole.ReviewerCodingStandards, DiffTemplate);
        _fixture.UseTemplate(AgentRole.ReviewerSpecification, DiffTemplate);
        SeededSpec spec = await _fixture.SeedReviewingAsync(1);
        CommitSha lastMerged = AdvanceIntegration(spec, "first-other-ticket.cs");
        _fixture.Clean(FindingAxis.CodingStandards).Issues(FindingAxis.Specification, ReviewLoopFixture.SpecFinding);
        CommitSha fixedHead = default;
        _fixture.Agents.Script(AgentRole.Implementer, request =>
        {
            _fixture.Execution.MergeIntegrationTipAsync(request, spec).GetAwaiter().GetResult();
            fixedHead = _fixture.Execution.CommitInWorktree(request, "fix.cs");
            AdvanceIntegration(spec, "second-other-ticket.cs");
            return ImplementationReport.Completed(fixedHead, "Fixed the findings.");
        });
        _fixture.Clean(FindingAxis.CodingStandards).Clean(FindingAxis.Specification);

        ReviewLoopResult result = await _fixture.RunLoopAsync(spec, 1);

        Assert.Equal(ReviewLoopResult.Integrating, result);
        AgentRunRequest[] secondRound = [.. _fixture.ReviewerRequests.TakeLast(2)];
        Assert.All(secondRound, request =>
        {
            Assert.StartsWith($"{lastMerged}|{fixedHead}|", request.Prompt, StringComparison.Ordinal);
            Assert.Contains("fix.cs", request.Prompt, StringComparison.Ordinal);
            Assert.DoesNotContain("other-ticket.cs", request.Prompt, StringComparison.Ordinal);
        });
    }

    /// <summary>Another ticket's integration saga moves the local integration ref by one layer.</summary>
    private CommitSha AdvanceIntegration(SeededSpec spec, string file)
    {
        CommitSha tip = _fixture.Git.GetBranchTipAsync(spec.Location, spec.IntegrationBranch, GitRefScope.Local, ReviewLoopFixture.Token).GetAwaiter().GetResult()!.Value;
        CommitSha layer = _fixture.Git.Commit([tip], file);
        _fixture.Git.UpdateBranchAsync(spec.Location, spec.IntegrationBranch, layer, tip, ReviewLoopFixture.Token).GetAwaiter().GetResult();
        return layer;
    }
}
