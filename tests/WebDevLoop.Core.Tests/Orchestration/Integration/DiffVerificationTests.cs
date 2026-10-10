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
    public async Task Stack_on_top_layer_without_a_published_blocking_layer_fails_verification_instead_of_showing_the_blockers_changes()
    {
        SpecRun blocker = _f.SeedRunningSpec();
        TicketRun blockerTicket = _f.SeedReviewedTicket(blocker, 1, "blocker.cs");
        await _f.IntegrateAsync(blockerTicket);
        // The dependent run started on the blocker's tip, but nothing links it to the blocking spec, so its base falls back to trunk.
        SpecRun dependent = _f.SeedRunningSpec(blocker.IntegrationTipSha, SpecDependencyMode.StackOnTop);
        TicketRun ticket = _f.SeedReviewedTicket(dependent, 2, "dependent.cs");

        IntegrationResult result = await _f.IntegrateAsync(ticket);

        Assert.Equal(IntegrationOutcome.NeedsAttention, result.Outcome);
        Assert.Contains($"base '{IntegrationFixture.Trunk}'", result.Reason, StringComparison.Ordinal);
        Assert.Equal(IssueState.Open, _f.IssueState(ticket));
    }
}
