using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Integration;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Orchestration.Integration;

public sealed class DiffVerificationTests
{
    private readonly IntegrationFixture _f = new();

    [Fact]
    public async Task Diff_verification_failure_stops_before_the_issue_transition()
    {
        SpecRun spec = _f.SeedRunningSpec();
        TicketRun bottom = _f.SeedReviewedTicket(spec, 1, "bottom.cs");
        TicketRun ticket = _f.SeedReviewedTicket(spec, 2, "upper.cs");
        await _f.IntegrateAsync(bottom);
        BranchName stackBranch = RunScopedNaming.StackBranch(spec.Id, ticket.Id);
        // Someone retargets the new PR onto trunk, so its diff would include the bottom layer as well.
        _f.Journal.AfterCall = call =>
        {
            if (call == $"create-pr:{stackBranch}")
            {
                PullRequestNumber pull = _f.Pulls.PullRequests.Single(candidate => candidate.Head == stackBranch).Number;
                _f.Pulls.UpdatePullRequestBaseAsync(IntegrationFixture.RepoRef, pull, IntegrationFixture.Trunk, IntegrationFixture.Token).GetAwaiter().GetResult();
            }
        };

        IntegrationResult result = await _f.IntegrateAsync(ticket);

        Assert.Equal(IntegrationOutcome.NeedsAttention, result.Outcome);
        Assert.Contains("Diff verification", result.Reason, StringComparison.Ordinal);
        Assert.Equal(TicketRunStatus.NeedsAttention, ticket.Status);
        Assert.Equal(AttentionCode.DiffVerificationFailed, ticket.Attention!.Code);
        Assert.Equal(IssueState.Open, _f.IssueState(ticket));
        Assert.Empty(_f.CallsOf($"close:{ticket.Issue}"));
        Assert.Empty(_f.Transitions(ticket, TicketRunStatus.Integrated));
        IntegrationSaga saga = _f.Saga(ticket)!;
        Assert.Equal(IntegrationSagaCheckpoint.StackLinked, saga.Checkpoint);
        Assert.Equal(result.Reason, saga.LastError);
        Assert.Null(_f.Layers(spec)[1].VerifiedDiffSha);
    }

    [Fact]
    public async Task Spec_whose_integration_base_is_not_an_ancestor_of_its_bottom_layer_base_fails_diff_verification()
    {
        SpecRun foreign = _f.SeedRunningSpec();
        TicketRun foreignTicket = _f.SeedReviewedTicket(foreign, 1, "foreign.cs");
        await _f.IntegrateAsync(foreignTicket);
        // An invalid integration base ahead of trunk would expose foreign changes in the bottom layer's diff.
        SpecRun spec = _f.SeedRunningSpec(foreign.IntegrationTipSha);
        TicketRun ticket = _f.SeedReviewedTicket(spec, 2, "ticket.cs");

        IntegrationResult result = await _f.IntegrateAsync(ticket);

        Assert.Equal(IntegrationOutcome.NeedsAttention, result.Outcome);
        Assert.Contains($"base '{IntegrationFixture.Trunk}'", result.Reason, StringComparison.Ordinal);
        Assert.Equal(IssueState.Open, _f.IssueState(ticket));
    }
}
