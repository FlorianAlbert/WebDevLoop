using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.ReviewLoop;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.ReviewLoop;

public sealed class TwoAxisReviewRunnerTests
{
    private const string ParentTemplate = "{review_axis} {review_scope} #{ticket_issue_number} {ticket_title} {diff_base_ref}...{diff_head_ref} {changed_files}";

    private readonly ReviewLoopFixture _fixture = new();

    [Fact]
    public async Task parent_spec_review_runs_both_axes_against_the_integration_branch()
    {
        _fixture.UseTemplate(AgentRole.ReviewerCodingStandards, ParentTemplate);
        _fixture.UseTemplate(AgentRole.ReviewerSpecification, ParentTemplate);
        SeededSpec spec = await _fixture.Execution.SeedRunningSpecAsync("app", (1, []));
        CommitSha baseSha = _fixture.Spec(spec.Id).IntegrationBaseSha!.Value;
        CommitSha integrated = _fixture.Git.Commit([baseSha], "ticket-1.cs");
        await _fixture.Git.UpdateBranchAsync(spec.Location, spec.IntegrationBranch, integrated, baseSha, ReviewLoopFixture.Token);
        const string checkout = "/work/runs/integration-checkout";
        _fixture.Clean(FindingAxis.CodingStandards).Issues(FindingAxis.Specification, ReviewLoopFixture.SpecFinding);

        ReviewRoundResult result = await _fixture.Reviews().RunAsync(
            new ReviewRequest(
                spec.Id,
                null,
                ReviewScope.ParentSpec,
                new ReviewTarget(checkout, spec.IntegrationBranch, baseSha, integrated),
                new ReviewRound(Attempt: 1, Iteration: 0),
                ReviewRequest.BothAxes),
            ReviewLoopFixture.Token);

        Assert.Equal(ReviewRoundOutcome.Completed, result.Outcome);
        Assert.False(result.IsClean);
        Assert.Equal([ReviewLoopFixture.SpecFinding], result.Findings);
        Assert.Equal([FindingAxis.CodingStandards, FindingAxis.Specification], result.Reports.Select(report => report.Axis));
        AgentRunRequest[] requests = _fixture.ReviewerRequests.OrderBy(request => request.Role).ToArray();
        Assert.Equal(
            [
                $"coding_standards parent_spec #n/a n/a {baseSha}...{integrated} ticket-1.cs",
                $"specification parent_spec #n/a n/a {baseSha}...{integrated} ticket-1.cs",
            ],
            requests.Select(request => request.Prompt));
        Assert.All(requests, request => Assert.Equal(checkout, request.Policy.Paths.WorkingDirectory));
        StepRun[] steps = _fixture.Db.Rows<StepRun>().Where(step => step.SpecRunId == spec.Id).OrderBy(step => step.AgentRole).ToArray();
        Assert.Equal(
            [(StepKind.ParentReview, AgentRole.ReviewerCodingStandards), (StepKind.ParentReview, AgentRole.ReviewerSpecification)],
            steps.Select(step => (step.Kind, step.AgentRole!.Value)));
        Assert.All(steps, step => Assert.Null(step.TicketRunId));
        Assert.All(steps, step => Assert.Equal(StepStatus.Succeeded, step.Status));
        Assert.All(steps, step =>
        {
            AgentRunRequest request = requests.Single(candidate => candidate.StepRunId == step.Id);
            Assert.False(string.IsNullOrWhiteSpace(step.Model));
            Assert.Equal((request.Settings.Model, request.Settings.ReasoningEffort), (step.Model, step.ReasoningEffort));
        });
    }

    [Fact]
    public async Task ticket_scope_requires_a_ticket()
    {
        SeededSpec spec = await _fixture.Execution.SeedRunningSpecAsync("app", (1, []));
        CommitSha tip = _fixture.Spec(spec.Id).IntegrationTipSha!.Value;

        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Reviews().RunAsync(
            new ReviewRequest(spec.Id, null, ReviewScope.Ticket, new ReviewTarget("/work/x", spec.IntegrationBranch, tip, tip), new ReviewRound(1, 0), ReviewRequest.BothAxes),
            ReviewLoopFixture.Token));
    }
}
