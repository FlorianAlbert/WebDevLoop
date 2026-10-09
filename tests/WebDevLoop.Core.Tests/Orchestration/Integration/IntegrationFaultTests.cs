using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Integration;
using WebDevLoop.Core.Orchestration.Results;

namespace WebDevLoop.Core.Tests.Orchestration.Integration;

/// <summary>Unexpected errors during the saga: nothing may stay running, and repeated faults hand the ticket to the user.</summary>
public sealed class IntegrationFaultTests
{
    private static readonly CommitSha Tip = new(new string('a', 40));

    private readonly IntegrationFixture _f = new();

    [Fact]
    public async Task Conflict_resolver_exception_finishes_its_step_as_failed_so_the_next_run_resumes_the_saga()
    {
        SpecRun spec = _f.SeedRunningSpec();
        TicketRun ticket = _f.SeedReviewedTicket(spec, 1, "feature.cs");
        _f.Git.ConflictOnNextSquash = ["shared.cs"];
        _f.Agents.Script(AgentRole.ConflictResolver, _ => throw new HttpRequestException("Copilot runtime unavailable."));

        IntegrationResult faulted = await _f.IntegrateAsync(ticket);
        IntegrationResult resumed = await _f.IntegrateAsync(ticket);

        Assert.Equal(IntegrationOutcome.Faulted, faulted.Outcome);
        StepRun resolver = Assert.Single(await _f.Store.ListByTicketRunAsync(ticket.Id, IntegrationFixture.Token));
        Assert.Equal((StepKind.ResolveConflict, StepStatus.Failed), (resolver.Kind, resolver.Status));
        Assert.Contains("Copilot runtime unavailable.", resolver.FailureReason, StringComparison.Ordinal);
        Assert.Equal(IntegrationOutcome.Integrated, resumed.Outcome);
        Assert.Equal(TicketRunStatus.Integrated, ticket.Status);
    }

    [Fact]
    public async Task Faults_in_a_row_beyond_max_retries_mark_the_ticket_needs_attention_so_the_user_can_retry()
    {
        _f.Settings.Defaults = _f.Settings.Defaults with { MaxRetries = 1 };
        SpecRun spec = _f.SeedRunningSpec();
        TicketRun ticket = _f.SeedReviewedTicket(spec, 1, "feature.cs");

        _f.JournaledPulls.FailNextCreate = new HttpRequestException("503 Service Unavailable");
        IntegrationResult first = await _f.IntegrateAsync(ticket);
        _f.JournaledPulls.FailNextCreate = new HttpRequestException("503 Service Unavailable");
        IntegrationResult second = await _f.IntegrateAsync(ticket);

        Assert.Equal(IntegrationOutcome.Faulted, first.Outcome);
        Assert.Equal(IntegrationOutcome.NeedsAttention, second.Outcome);
        Assert.Equal(TicketRunStatus.NeedsAttention, ticket.Status);
        Assert.Contains("503", ticket.FailureReason, StringComparison.Ordinal);
        Assert.Single(_f.Transitions(ticket, TicketRunStatus.NeedsAttention));
    }

    [Fact]
    public void Consecutive_faults_count_until_the_saga_makes_progress()
    {
        IntegrationSaga saga = IntegrationSaga.Start(new RunId("run-1"), new TicketRunId("ticket-1"), Tip, IntegrationFixture.T0);

        saga.RecordFault("503", IntegrationFixture.T0);
        saga.RecordFault("503", IntegrationFixture.T0);
        int beforeProgress = saga.ConsecutiveFaults;
        saga.AdvanceTo(IntegrationSagaCheckpoint.SquashCommitCreated, IntegrationFixture.T0);
        int afterProgress = saga.ConsecutiveFaults;
        saga.RecordFault("503", IntegrationFixture.T0);
        saga.RetargetTo(Tip, IntegrationFixture.T0);

        Assert.Equal((2, 0, 0), (beforeProgress, afterProgress, saga.ConsecutiveFaults));
    }
}
