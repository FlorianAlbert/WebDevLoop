using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Control;
using WebDevLoop.Core.Orchestration.Integration;

namespace WebDevLoop.Core.Tests.Orchestration.Control;

public sealed class SpecRunControlTests : IDisposable
{
    private static readonly SpecRunStatus[] ToRunning = [SpecRunStatus.Preparing, SpecRunStatus.Running];
    private static readonly SpecRunStatus[] ToParentReview = [.. ToRunning, SpecRunStatus.ParentReviewing];
    private static readonly SpecRunStatus[] ToTesting = [.. ToParentReview, SpecRunStatus.Testing];
    private static readonly SpecRunStatus[] ToAwaitingMerge = [.. ToTesting, SpecRunStatus.ReadyForReview, SpecRunStatus.AwaitingMerge];

    private RunControlFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Retrying_a_failed_preparation_prepares_again_in_a_free_slot_and_is_audited()
    {
        SpecRun spec = _fixture.SeedFailedSpec(SpecRunStatus.Preparing);

        ControlResult result = await _fixture.Control().RetrySpecAsync(spec.Id, RunControlFixture.Token);

        Assert.Equal(ControlOutcome.Applied, result.Outcome);
        Assert.Equal((SpecRunStatus.Preparing, 1), (spec.Status, spec.MaxActiveSpecsSlot));
        Assert.Null(spec.FailureReason);
        SpecRunStatusChanged changed = Assert.Single(_fixture.Events.OfType<SpecRunStatusChanged>());
        Assert.Equal((SpecRunStatus.NeedsAttention, SpecRunStatus.Preparing), (changed.From, changed.To));
        Assert.Equal(new RunControlApplied(spec.Id, null, ControlAction.Retry, RunControlFixture.T0), Assert.Single(_fixture.Events.OfType<RunControlApplied>()));
        RunEvent audit = Assert.Single(_fixture.AuditOf(spec));
        Assert.Equal(RunControlJournal.RunEventType(ControlAction.Retry), audit.Type);
        Assert.Contains("\"status\":\"Preparing\"", audit.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Retrying_a_failed_parent_review_reviews_again_when_every_ticket_is_done()
    {
        SpecRun spec = _fixture.SeedFailedSpec(ToParentReview);
        _fixture.SeedTicket(spec, TicketRunStatus.Ready, TicketRunStatus.Implementing, TicketRunStatus.Reviewing, TicketRunStatus.Integrating, TicketRunStatus.Integrated);
        _fixture.SeedTicket(spec, TicketRunStatus.Skipped);

        await _fixture.Control().RetrySpecAsync(spec.Id, RunControlFixture.Token);

        Assert.Equal((SpecRunStatus.ParentReviewing, 2, 1), (spec.Status, spec.ReviewCycle, spec.MaxActiveSpecsSlot));
    }

    [Theory]
    [InlineData(SpecRunStatus.ParentReviewing)]
    [InlineData(SpecRunStatus.Testing)]
    public async Task Retrying_after_findings_hit_the_cycle_limit_works_the_finding_tickets_first(SpecRunStatus failedIn)
    {
        SpecRun spec = _fixture.SeedFailedSpec(failedIn == SpecRunStatus.Testing ? ToTesting : ToParentReview);
        _fixture.SeedTicket(spec);

        await _fixture.Control().RetrySpecAsync(spec.Id, RunControlFixture.Token);

        Assert.Equal(SpecRunStatus.Running, spec.Status);
    }

    [Fact]
    public async Task Retrying_a_failed_test_cycle_tests_again()
    {
        SpecRun spec = _fixture.SeedFailedSpec(ToTesting);

        await _fixture.Control().RetrySpecAsync(spec.Id, RunControlFixture.Token);

        Assert.Equal((SpecRunStatus.Testing, 2), (spec.Status, spec.TestCycle));
    }

    [Fact]
    public async Task Retrying_a_failed_merge_tracking_restarts_it_from_ready_for_review_without_claiming_a_slot()
    {
        SpecRun spec = _fixture.SeedFailedSpec(ToAwaitingMerge);
        _fixture.SeedSpec(1, ToRunning);
        _fixture.Clock.Advance(TimeSpan.FromDays(2));

        ControlResult result = await _fixture.Control().RetrySpecAsync(spec.Id, RunControlFixture.Token);

        Assert.True(result.IsApplied);
        Assert.Equal(SpecRunStatus.ReadyForReview, spec.Status);
        Assert.Null(spec.MaxActiveSpecsSlot);

        // Merge tracking measures the trunk-containment timeout from ReadyAt, so the retry gets a fresh window.
        Assert.Equal(_fixture.Clock.UtcNow, spec.ReadyAt);
    }

    [Fact]
    public async Task Retrying_claims_a_free_slot_instead_of_the_stale_one_another_spec_took_meanwhile()
    {
        _fixture.Dispose();
        _fixture = new RunControlFixture(maxActiveSpecs: 2);
        SpecRun failed = _fixture.SeedFailedSpec(ToRunning);
        SpecRun other = _fixture.SeedSpec(1, ToRunning);

        await _fixture.Control().RetrySpecAsync(failed.Id, RunControlFixture.Token);

        Assert.Equal((SpecRunStatus.Running, 2), (failed.Status, failed.MaxActiveSpecsSlot));
        Assert.Equal(1, other.MaxActiveSpecsSlot);
    }

    [Fact]
    public async Task Retrying_is_refused_while_every_active_slot_is_taken()
    {
        SpecRun failed = _fixture.SeedFailedSpec(ToRunning);
        _fixture.SeedSpec(1, ToRunning);

        ControlResult result = await _fixture.Control().RetrySpecAsync(failed.Id, RunControlFixture.Token);

        Assert.Equal(ControlOutcome.NoActiveSlot, result.Outcome);
        Assert.Equal(SpecRunStatus.NeedsAttention, failed.Status);
        Assert.Null(failed.MaxActiveSpecsSlot);
        Assert.Empty(_fixture.Events);
        Assert.Empty(_fixture.AuditOf(failed));
    }

    [Fact]
    public async Task Only_a_spec_that_needs_attention_can_be_retried()
    {
        SpecRun spec = _fixture.SeedSpec(1, ToRunning);

        ControlResult result = await _fixture.Control().RetrySpecAsync(spec.Id, RunControlFixture.Token);

        Assert.Equal(ControlOutcome.NotAllowed, result.Outcome);
        Assert.Equal(SpecRunStatus.Running, spec.Status);
        Assert.Empty(_fixture.Events);
    }

    [Fact]
    public async Task Control_actions_on_an_unknown_spec_are_not_found()
    {
        IRunControl control = _fixture.Control();

        Assert.Equal(ControlOutcome.NotFound, (await control.RetrySpecAsync(new RunId("nope"), RunControlFixture.Token)).Outcome);
        Assert.Equal(ControlOutcome.NotFound, (await control.AbortSpecAsync(new RunId("nope"), RunControlFixture.Token)).Outcome);
    }

    [Fact]
    public async Task A_retry_that_loses_a_concurrent_update_changes_nothing()
    {
        SpecRun spec = _fixture.SeedFailedSpec(SpecRunStatus.Preparing);
        _fixture.Store.ConflictOnNextSave = true;

        ControlResult result = await _fixture.Control().RetrySpecAsync(spec.Id, RunControlFixture.Token);

        Assert.Equal(ControlOutcome.ConcurrencyConflict, result.Outcome);
        Assert.Empty(_fixture.Events);
    }

    [Fact]
    public async Task Aborting_stops_agent_sessions_and_the_tester_app_and_releases_slot_and_lease()
    {
        SpecRun spec = _fixture.SeedSpec(1, ToRunning);
        TicketRun implementing = _fixture.SeedTicket(spec, TicketRunStatus.Ready, TicketRunStatus.Implementing);
        TicketRun integrated = _fixture.SeedTicket(spec, TicketRunStatus.Ready, TicketRunStatus.Implementing, TicketRunStatus.Reviewing, TicketRunStatus.Integrating, TicketRunStatus.Integrated);
        TicketRun blocked = _fixture.SeedTicket(spec);
        StepRun implementStep = _fixture.SeedRunningStep(spec, implementing, StepKind.Implement, AgentRole.Implementer);
        StepRun testStep = _fixture.SeedRunningStep(spec, null, StepKind.Test, AgentRole.Tester);
        TestLease lease = _fixture.SeedLease(spec, 41003);

        ControlResult result = await _fixture.Control().AbortSpecAsync(spec.Id, RunControlFixture.Token);

        Assert.True(result.IsApplied);
        Assert.Equal((SpecRunStatus.Aborted, (int?)null), (spec.Status, spec.MaxActiveSpecsSlot));
        Assert.Equal(
            [TicketRunStatus.Aborted, TicketRunStatus.Integrated, TicketRunStatus.Aborted],
            new[] { implementing.Status, integrated.Status, blocked.Status });
        Assert.Equal([StepStatus.Cancelled, StepStatus.Cancelled], new[] { implementStep.Status, testStep.Status });
        Assert.Equal([implementStep.CopilotSessionId, testStep.CopilotSessionId], _fixture.AbortedSessions.Select(session => session.Value));
        Assert.False(lease.IsActive);
        Assert.Equal((spec.Id, 41003), (Assert.Single(_fixture.Targets.Stopped).Target.SpecRunId, _fixture.Targets.Stopped[0].Target.Port));
        Assert.Equal((SpecRunStatus.Running, SpecRunStatus.Aborted), _fixture.Events.OfType<SpecRunStatusChanged>().Select(e => (e.From, e.To)).Single());
        Assert.Equal([implementing.Id, blocked.Id], _fixture.Events.OfType<TicketRunStatusChanged>().Where(e => e.To == TicketRunStatus.Aborted).Select(e => e.TicketRunId));
        Assert.Equal(2, _fixture.Events.OfType<StepRunStatusChanged>().Count(e => e.Status == StepStatus.Cancelled));
        Assert.Single(_fixture.Events.OfType<RunControlApplied>(), e => e.Action == ControlAction.Abort);
        Assert.Equal(RunControlJournal.RunEventType(ControlAction.Abort), Assert.Single(_fixture.AuditOf(spec)).Type);
    }

    [Fact]
    public async Task Aborting_a_queued_spec_takes_it_out_of_the_queue()
    {
        SpecRun spec = _fixture.SeedSpec();

        ControlResult result = await _fixture.Control().AbortSpecAsync(spec.Id, RunControlFixture.Token);

        Assert.True(result.IsApplied);
        Assert.Equal(SpecRunStatus.Aborted, spec.Status);
    }

    [Theory]
    [InlineData(SpecRunStatus.Completed)]
    [InlineData(SpecRunStatus.Aborted)]
    public async Task A_finished_spec_cannot_be_aborted(SpecRunStatus finished)
    {
        SpecRun spec = _fixture.SeedSpec(1, finished == SpecRunStatus.Completed ? [.. ToAwaitingMerge, finished] : [finished]);

        ControlResult result = await _fixture.Control().AbortSpecAsync(spec.Id, RunControlFixture.Token);

        Assert.Equal(ControlOutcome.NotAllowed, result.Outcome);
        Assert.Empty(_fixture.Events);
    }

    [Fact]
    public async Task Aborting_a_spec_with_an_integrating_ticket_while_a_merge_of_the_repository_runs_is_a_conflict()
    {
        SpecRun spec = _fixture.SeedSpec(1, ToRunning);
        TicketRun integrating = _fixture.SeedTicket(spec, TicketRunStatus.Ready, TicketRunStatus.Implementing, TicketRunStatus.Reviewing, TicketRunStatus.Integrating);
        _fixture.SeedSaga(integrating, IntegrationSagaCheckpoint.IntegrationPushed);
        _fixture.Options = new RunControlOptions(TimeSpan.FromMilliseconds(20));
        using IDisposable merge = await _fixture.Gate.EnterAsync(_fixture.Repository.Id, RunControlFixture.Token);

        ControlResult result = await _fixture.Control().AbortSpecAsync(spec.Id, RunControlFixture.Token);

        Assert.Equal(ControlOutcome.ConcurrencyConflict, result.Outcome);
        Assert.Equal((SpecRunStatus.Running, TicketRunStatus.Integrating), (spec.Status, integrating.Status));
        Assert.Empty(_fixture.Events);
    }

    [Fact]
    public async Task Aborting_a_spec_with_an_integrating_ticket_once_the_merge_finished_aborts_it_and_frees_the_merge_lock()
    {
        SpecRun spec = _fixture.SeedSpec(1, ToRunning);
        TicketRun integrating = _fixture.SeedTicket(spec, TicketRunStatus.Ready, TicketRunStatus.Implementing, TicketRunStatus.Reviewing, TicketRunStatus.Integrating);
        _fixture.SeedSaga(integrating, IntegrationSagaCheckpoint.IntegrationPushed);

        ControlResult result = await _fixture.Control().AbortSpecAsync(spec.Id, RunControlFixture.Token);

        Assert.True(result.IsApplied);
        Assert.Equal((SpecRunStatus.Aborted, TicketRunStatus.Aborted), (spec.Status, integrating.Status));
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(RunControlFixture.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using IDisposable merge = await _fixture.Gate.EnterAsync(_fixture.Repository.Id, timeout.Token);
    }

    [Fact]
    public async Task An_abort_that_loses_a_concurrent_update_stops_nothing()
    {
        SpecRun spec = _fixture.SeedSpec(1, ToRunning);
        TicketRun implementing = _fixture.SeedTicket(spec, TicketRunStatus.Ready, TicketRunStatus.Implementing);
        _fixture.SeedRunningStep(spec, implementing, StepKind.Implement, AgentRole.Implementer);
        _fixture.SeedLease(spec, 41003);
        _fixture.Store.ConflictOnNextSave = true;

        ControlResult result = await _fixture.Control().AbortSpecAsync(spec.Id, RunControlFixture.Token);

        Assert.Equal(ControlOutcome.ConcurrencyConflict, result.Outcome);
        Assert.Empty(_fixture.AbortedSessions);
        Assert.Empty(_fixture.Targets.Stopped);
        Assert.Empty(_fixture.Events);
    }
}
