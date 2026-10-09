using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Control;

namespace WebDevLoop.Core.Tests.Orchestration.Control;

public sealed class TicketRunControlTests : IDisposable
{
    private static readonly SpecRunStatus[] ToRunning = [SpecRunStatus.Preparing, SpecRunStatus.Running];
    private static readonly TicketRunStatus[] ToImplementing = [TicketRunStatus.Ready, TicketRunStatus.Implementing];
    private static readonly TicketRunStatus[] ToReviewing = [.. ToImplementing, TicketRunStatus.Reviewing];
    private static readonly TicketRunStatus[] ToIntegrating = [.. ToReviewing, TicketRunStatus.Integrating];

    private readonly RunControlFixture _fixture = new();
    private readonly SpecRun _spec;

    public TicketRunControlTests() => _spec = _fixture.SeedSpec(1, ToRunning);

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Retrying_a_ticket_whose_implementation_failed_makes_it_ready_again_and_is_audited()
    {
        TicketRun ticket = _fixture.SeedFailedTicket(_spec, ToImplementing);

        ControlResult result = await _fixture.Control().RetryTicketAsync(ticket.Id, RunControlFixture.Token);

        Assert.True(result.IsApplied);
        Assert.Equal(TicketRunStatus.Ready, ticket.Status);
        TicketRunStatusChanged changed = Assert.Single(_fixture.Events.OfType<TicketRunStatusChanged>());
        Assert.Equal((ticket.Id, TicketRunStatus.NeedsAttention, TicketRunStatus.Ready), (changed.TicketRunId, changed.From, changed.To));
        Assert.Equal(new RunControlApplied(_spec.Id, ticket.Id, ControlAction.Retry, RunControlFixture.T0), Assert.Single(_fixture.Events.OfType<RunControlApplied>()));
        RunEvent audit = Assert.Single(_fixture.AuditOf(_spec));
        Assert.Equal((RunControlJournal.RunEventType(ControlAction.Retry), ticket.Id), (audit.Type, audit.TicketRunId));
    }

    [Fact]
    public async Task Retrying_a_ticket_whose_review_failed_reviews_it_again()
    {
        TicketRun ticket = _fixture.SeedFailedTicket(_spec, [.. ToReviewing, TicketRunStatus.FixingReviewFindings]);

        await _fixture.Control().RetryTicketAsync(ticket.Id, RunControlFixture.Token);

        Assert.Equal((TicketRunStatus.Reviewing, 2, 0), (ticket.Status, ticket.Attempt, ticket.ReviewIteration));
    }

    [Fact]
    public async Task Retrying_a_ticket_whose_integration_failed_resumes_the_integration()
    {
        TicketRun ticket = _fixture.SeedFailedTicket(_spec, ToIntegrating);

        await _fixture.Control().RetryTicketAsync(ticket.Id, RunControlFixture.Token);

        Assert.Equal(TicketRunStatus.Integrating, ticket.Status);
    }

    [Fact]
    public async Task Only_a_ticket_that_needs_attention_can_be_retried()
    {
        TicketRun ticket = _fixture.SeedTicket(_spec, ToImplementing);

        ControlResult result = await _fixture.Control().RetryTicketAsync(ticket.Id, RunControlFixture.Token);

        Assert.Equal(ControlOutcome.NotAllowed, result.Outcome);
        Assert.Equal(TicketRunStatus.Implementing, ticket.Status);
        Assert.Empty(_fixture.Events);
    }

    [Fact]
    public async Task Tickets_of_a_finished_spec_cannot_be_controlled()
    {
        SpecRun aborted = _fixture.SeedSpec(1, SpecRunStatus.Aborted);
        TicketRun ticket = _fixture.SeedFailedTicket(aborted, ToImplementing);
        IRunControl control = _fixture.Control();

        Assert.Equal(ControlOutcome.NotAllowed, (await control.RetryTicketAsync(ticket.Id, RunControlFixture.Token)).Outcome);
        Assert.Equal(ControlOutcome.NotAllowed, (await control.SkipTicketAsync(ticket.Id, SkipDependents.Unblock, RunControlFixture.Token)).Outcome);
        Assert.Equal(ControlOutcome.NotAllowed, (await control.AbortTicketAsync(ticket.Id, RunControlFixture.Token)).Outcome);
        Assert.Equal(TicketRunStatus.NeedsAttention, ticket.Status);
    }

    [Fact]
    public async Task Control_actions_on_an_unknown_ticket_are_not_found()
    {
        IRunControl control = _fixture.Control();
        var unknown = new TicketRunId("nope");

        Assert.Equal(ControlOutcome.NotFound, (await control.RetryTicketAsync(unknown, RunControlFixture.Token)).Outcome);
        Assert.Equal(ControlOutcome.NotFound, (await control.SkipTicketAsync(unknown, SkipDependents.Unblock, RunControlFixture.Token)).Outcome);
        Assert.Equal(ControlOutcome.NotFound, (await control.AbortTicketAsync(unknown, RunControlFixture.Token)).Outcome);
    }

    [Fact]
    public async Task Skipping_a_ticket_leaves_its_dependents_to_the_frontier_by_default()
    {
        TicketRun ticket = _fixture.SeedFailedTicket(_spec, ToImplementing);
        TicketRun dependent = _fixture.SeedTicket(_spec);
        _fixture.Block(dependent, ticket);

        ControlResult result = await _fixture.Control().SkipTicketAsync(ticket.Id, SkipDependents.Unblock, RunControlFixture.Token);

        Assert.True(result.IsApplied);
        Assert.Equal((TicketRunStatus.Skipped, TicketRunStatus.Blocked), (ticket.Status, dependent.Status));
        TicketRunStatusChanged changed = Assert.Single(_fixture.Events.OfType<TicketRunStatusChanged>());
        Assert.Equal((TicketRunStatus.NeedsAttention, TicketRunStatus.Skipped), (changed.From, changed.To));
        Assert.Equal(RunControlJournal.RunEventType(ControlAction.Skip), Assert.Single(_fixture.AuditOf(_spec)).Type);
    }

    [Fact]
    public async Task Skipping_with_dependents_skips_every_not_yet_started_ticket_that_depends_on_it()
    {
        TicketRun ticket = _fixture.SeedTicket(_spec, TicketRunStatus.Ready);
        TicketRun child = _fixture.SeedTicket(_spec);
        TicketRun grandchild = _fixture.SeedFailedTicket(_spec, ToImplementing);
        TicketRun unrelated = _fixture.SeedTicket(_spec);
        _fixture.Block(child, ticket);
        _fixture.Block(grandchild, child);

        await _fixture.Control().SkipTicketAsync(ticket.Id, SkipDependents.Skip, RunControlFixture.Token);

        Assert.Equal(
            [TicketRunStatus.Skipped, TicketRunStatus.Skipped, TicketRunStatus.Skipped, TicketRunStatus.Blocked],
            new[] { ticket.Status, child.Status, grandchild.Status, unrelated.Status });
        Assert.Equal([ticket.Id, child.Id, grandchild.Id], _fixture.Events.OfType<TicketRunStatusChanged>().Select(e => e.TicketRunId));
        Assert.Contains($"\"{grandchild.Id.Value}\"", Assert.Single(_fixture.AuditOf(_spec)).PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_ticket_that_is_being_worked_on_cannot_be_skipped()
    {
        TicketRun ticket = _fixture.SeedTicket(_spec, ToImplementing);

        ControlResult result = await _fixture.Control().SkipTicketAsync(ticket.Id, SkipDependents.Unblock, RunControlFixture.Token);

        Assert.Equal(ControlOutcome.NotAllowed, result.Outcome);
        Assert.Equal(TicketRunStatus.Implementing, ticket.Status);
    }

    [Theory]
    [InlineData(ControlAction.Skip)]
    [InlineData(ControlAction.Abort)]
    public async Task A_ticket_whose_squash_is_already_on_the_integration_branch_must_be_retried_instead(ControlAction action)
    {
        TicketRun ticket = _fixture.SeedFailedTicket(_spec, ToIntegrating);
        _fixture.SeedSaga(ticket, IntegrationSagaCheckpoint.IntegrationPushed);
        IRunControl control = _fixture.Control();

        ControlResult result = action == ControlAction.Skip
            ? await control.SkipTicketAsync(ticket.Id, SkipDependents.Unblock, RunControlFixture.Token)
            : await control.AbortTicketAsync(ticket.Id, RunControlFixture.Token);

        Assert.Equal(ControlOutcome.NotAllowed, result.Outcome);
        Assert.Contains("integration branch", result.Reason, StringComparison.Ordinal);
        Assert.Equal(TicketRunStatus.NeedsAttention, ticket.Status);
    }

    [Fact]
    public async Task A_ticket_whose_saga_has_not_moved_the_integration_branch_can_be_skipped()
    {
        TicketRun ticket = _fixture.SeedFailedTicket(_spec, ToIntegrating);
        _fixture.SeedSaga(ticket, IntegrationSagaCheckpoint.SquashCommitCreated);

        ControlResult result = await _fixture.Control().SkipTicketAsync(ticket.Id, SkipDependents.Unblock, RunControlFixture.Token);

        Assert.True(result.IsApplied);
    }

    [Fact]
    public async Task Aborting_a_ticket_stops_its_agent_session_and_keeps_its_dependents_blocked()
    {
        TicketRun ticket = _fixture.SeedTicket(_spec, ToImplementing);
        TicketRun dependent = _fixture.SeedTicket(_spec);
        _fixture.Block(dependent, ticket);
        StepRun step = _fixture.SeedRunningStep(_spec, ticket, StepKind.Implement, AgentRole.Implementer);

        ControlResult result = await _fixture.Control().AbortTicketAsync(ticket.Id, RunControlFixture.Token);

        Assert.True(result.IsApplied);
        Assert.Equal((TicketRunStatus.Aborted, TicketRunStatus.Blocked, StepStatus.Cancelled), (ticket.Status, dependent.Status, step.Status));
        Assert.Equal(step.CopilotSessionId, Assert.Single(_fixture.AbortedSessions).Value);
        Assert.Equal(SpecRunStatus.Running, _spec.Status);
        Assert.Equal((TicketRunStatus.Implementing, TicketRunStatus.Aborted), _fixture.Events.OfType<TicketRunStatusChanged>().Select(e => (e.From, e.To)).Single());
        Assert.Single(_fixture.Events.OfType<StepRunStatusChanged>(), e => e.Status == StepStatus.Cancelled);
        Assert.Equal(RunControlJournal.RunEventType(ControlAction.Abort), Assert.Single(_fixture.AuditOf(_spec)).Type);
    }

    [Fact]
    public async Task Aborting_an_integrating_ticket_while_a_merge_of_the_repository_runs_is_a_conflict()
    {
        TicketRun ticket = _fixture.SeedTicket(_spec, ToIntegrating);
        _fixture.Options = new RunControlOptions(TimeSpan.FromMilliseconds(20));
        using IDisposable merge = await _fixture.Gate.EnterAsync(_fixture.Repository.Id, RunControlFixture.Token);

        ControlResult result = await _fixture.Control().AbortTicketAsync(ticket.Id, RunControlFixture.Token);

        Assert.Equal(ControlOutcome.ConcurrencyConflict, result.Outcome);
        Assert.Equal(TicketRunStatus.Integrating, ticket.Status);
        Assert.Empty(_fixture.Events);
    }

    [Fact]
    public async Task Aborting_an_integrating_ticket_before_its_squash_moved_the_branch_is_allowed()
    {
        TicketRun ticket = _fixture.SeedTicket(_spec, ToIntegrating);
        _fixture.SeedSaga(ticket, IntegrationSagaCheckpoint.Started);

        ControlResult result = await _fixture.Control().AbortTicketAsync(ticket.Id, RunControlFixture.Token);

        Assert.True(result.IsApplied);
        Assert.Equal(TicketRunStatus.Aborted, ticket.Status);
    }

    [Fact]
    public async Task An_aborted_ticket_cannot_be_aborted_again()
    {
        TicketRun ticket = _fixture.SeedTicket(_spec, TicketRunStatus.Aborted);

        ControlResult result = await _fixture.Control().AbortTicketAsync(ticket.Id, RunControlFixture.Token);

        Assert.Equal(ControlOutcome.NotAllowed, result.Outcome);
    }

    [Fact]
    public async Task A_ticket_abort_that_loses_a_concurrent_update_stops_nothing()
    {
        TicketRun ticket = _fixture.SeedTicket(_spec, ToImplementing);
        _fixture.SeedRunningStep(_spec, ticket, StepKind.Implement, AgentRole.Implementer);
        _fixture.Store.ConflictOnNextSave = true;

        ControlResult result = await _fixture.Control().AbortTicketAsync(ticket.Id, RunControlFixture.Token);

        Assert.Equal(ControlOutcome.ConcurrencyConflict, result.Outcome);
        Assert.Empty(_fixture.AbortedSessions);
        Assert.Empty(_fixture.Events);
    }
}
