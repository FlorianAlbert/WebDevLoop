using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Integration;
using WebDevLoop.Core.Orchestration.Recovery.ExternalState;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Tests.Orchestration.Integration;

namespace WebDevLoop.Core.Tests.Orchestration.Recovery.ExternalState;

/// <summary>
/// A needs-attention ticket whose squash commit already moved the integration branch blocks every later layer of its spec
/// (they wait for its layer), so reconciliation resumes its saga once the retry interval has passed.
/// </summary>
public sealed class ParkedIntegrationTests
{
    private readonly ExternalStateFixture _x = new();

    private IntegrationFixture F => _x.Integration;

    [Fact]
    public async Task Parked_ticket_whose_commit_moved_the_integration_branch_is_resumed_after_the_retry_interval()
    {
        (SpecRun spec, TicketRun first, TicketRun second) = await ParkFirstOfTwoAsync();
        F.Clock.Advance(_x.Options.ParkedIntegrationRetryInterval);

        ExternalReconciliationReport report = await _x.ReconcileAsync();

        Assert.Equal(TicketRunStatus.Integrated, first.Status);
        Assert.Equal(first.Id, Assert.Single(F.Layers(spec)).TicketRunId);
        Assert.Single(_x.Pending<TicketRunStatusChanged>(), changed =>
            changed.TicketRunId == first.Id && changed is { From: TicketRunStatus.NeedsAttention, To: TicketRunStatus.Integrating });
        Assert.Contains(report.Actions, action => action is { Kind: ReconciliationActionKind.ParkedIntegrationResumed } && action.TicketRunId == first.Id);
        Assert.Contains(F.AssignmentFor(second), _x.Launcher.Launched);
    }

    [Fact]
    public async Task Parked_ticket_stays_parked_within_the_retry_interval()
    {
        (_, TicketRun first, _) = await ParkFirstOfTwoAsync();
        F.Clock.Advance(_x.Options.ParkedIntegrationRetryInterval - TimeSpan.FromSeconds(1));

        ExternalReconciliationReport report = await _x.ReconcileAsync();

        Assert.Equal(TicketRunStatus.NeedsAttention, first.Status);
        Assert.Empty(F.CallsOf("create-pr:"));
        Assert.DoesNotContain(report.Actions, action => action.TicketRunId == first.Id);
    }

    [Fact]
    public async Task Needs_attention_ticket_whose_saga_never_moved_the_integration_branch_is_left_to_the_user()
    {
        SpecRun spec = _x.RunningSpec();
        TicketRun ticket = F.SeedReviewedTicket(spec, 1, "feature.cs");
        F.Git.ConflictOnNextSquash = ["shared.cs"];
        F.Agents.Script(AgentRole.ConflictResolver, _ => new ConflictResolutionReport(ConflictResolutionStatus.Blocked, null, [], "Needs a product decision.", []));
        Assert.Equal(IntegrationOutcome.NeedsAttention, (await F.IntegrateAsync(ticket)).Outcome);
        F.Clock.Advance(TimeSpan.FromDays(1));

        ExternalReconciliationReport report = await _x.ReconcileAsync();

        Assert.Equal(TicketRunStatus.NeedsAttention, ticket.Status);
        Assert.Single(F.CallsOf("squash"));
        Assert.Empty(_x.Launcher.Launched);
        Assert.DoesNotContain(report.Actions, action => action.TicketRunId == ticket.Id);
    }

    /// <summary>
    /// The first ticket faults after moving and pushing the integration branch and is then parked as needing attention;
    /// the second ticket waits for the first ticket's layer.
    /// </summary>
    private async Task<(SpecRun Spec, TicketRun First, TicketRun Second)> ParkFirstOfTwoAsync()
    {
        SpecRun spec = _x.RunningSpec();
        TicketRun first = F.SeedReviewedTicket(spec, 1, "first.cs");
        TicketRun second = F.SeedReviewedTicket(spec, 2, "second.cs");
        F.JournaledPulls.FailNextCreate = new HttpRequestException("503 Service Unavailable");
        Assert.Equal(IntegrationOutcome.Faulted, (await F.IntegrateAsync(first)).Outcome);
        first.MarkNeedsAttention(AttentionReasons.Unclassified("Integration failed 4 time(s) in a row at checkpoint StackBranchPushed: 503", true), IntegrationFixture.T0);
        Assert.Equal(IntegrationOutcome.WaitingForEarlierLayer, (await F.IntegrateAsync(second)).Outcome);
        return (spec, first, second);
    }
}
