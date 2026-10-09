using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Integration;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Orchestration.Integration;

/// <summary>A run aborted while its saga is between side effects must not get pushes, PRs, stack links, or closed issues afterwards.</summary>
public sealed class IntegrationAbortTests
{
    /// <summary>squash, integration ref, integration push, stack push, PR, stack link; the issue close is the last one.</summary>
    private const int SideEffectsBeforeIssueClose = 6;

    public static TheoryData<int> EverySideEffectBeforeIssueClose => [.. Enumerable.Range(1, SideEffectsBeforeIssueClose)];

    [Theory]
    [MemberData(nameof(EverySideEffectBeforeIssueClose))]
    public async Task Saga_stops_before_the_next_side_effect_once_the_spec_was_aborted(int abortAfterCall)
    {
        var f = new IntegrationFixture();
        (SpecRun spec, TicketRun ticket, int before) = await SeedUpperLayerAsync(f);
        f.Journal.AfterCall = _ =>
        {
            if (f.Journal.Calls.Count == before + abortAfterCall)
            {
                // Another scope (Abort) commits while the saga is between two side effects.
                spec.TransitionTo(SpecRunStatus.Aborted, IntegrationFixture.T0);
                ticket.TransitionTo(TicketRunStatus.Aborted, IntegrationFixture.T0);
            }
        };

        IntegrationResult result = await f.IntegrateAsync(ticket);

        Assert.Equal(abortAfterCall, f.Journal.Calls.Count - before);
        Assert.Equal(IssueState.Open, f.IssueState(ticket));
        Assert.Equal(IntegrationOutcome.NotIntegrating, result.Outcome);
        Assert.Empty(f.Transitions(ticket, TicketRunStatus.Integrated));
        Assert.False(f.Saga(ticket)!.IsCompleted);
    }

    [Fact]
    public async Task Saga_stops_once_its_spec_is_no_longer_active_even_if_the_ticket_still_reads_integrating()
    {
        var f = new IntegrationFixture();
        (SpecRun spec, TicketRun ticket, int before) = await SeedUpperLayerAsync(f);
        f.Journal.AfterCall = call =>
        {
            if (call.StartsWith("update-ref:", StringComparison.Ordinal))
            {
                spec.TransitionTo(SpecRunStatus.Aborted, IntegrationFixture.T0);
            }
        };

        IntegrationResult result = await f.IntegrateAsync(ticket);

        Assert.StartsWith("update-ref:", f.Journal.Calls.Skip(before).Last(), StringComparison.Ordinal);
        Assert.Equal(IssueState.Open, f.IssueState(ticket));
        Assert.Equal(IntegrationOutcome.NotIntegrating, result.Outcome);
        Assert.Equal(TicketRunStatus.Integrating, ticket.Status);
    }

    /// <summary>A spec whose bottom layer is integrated, and a reviewed ticket that will become the second layer.</summary>
    private static async Task<(SpecRun Spec, TicketRun Ticket, int CallsBefore)> SeedUpperLayerAsync(IntegrationFixture f)
    {
        SpecRun spec = f.SeedRunningSpec();
        TicketRun bottom = f.SeedReviewedTicket(spec, 1, "bottom.cs");
        TicketRun ticket = f.SeedReviewedTicket(spec, 2, "upper.cs");
        Assert.Equal(IntegrationOutcome.Integrated, (await f.IntegrateAsync(bottom)).Outcome);
        return (spec, ticket, f.Journal.Calls.Count);
    }
}
