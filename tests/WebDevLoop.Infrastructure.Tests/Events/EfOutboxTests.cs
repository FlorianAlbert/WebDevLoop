using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Events;
using WebDevLoop.Infrastructure.Tests.Persistence;

namespace WebDevLoop.Infrastructure.Tests.Events;

public sealed class EfOutboxTests : IDisposable
{
    private static readonly DateTimeOffset Now = TestData.Now;

    private readonly PersistenceHarness _harness = new();
    private readonly FixedClock _clock = new(Now.AddMinutes(5));

    public static TheoryData<WorkflowEvent> AllEvents() => new()
    {
        new SpecRunStatusChanged(new RunId("run-1"), 3, SpecRunStatus.Queued, SpecRunStatus.Preparing, Now),
        new TicketRunStatusChanged(new RunId("run-1"), new TicketRunId("t-1"), TicketRunStatus.Ready, TicketRunStatus.Implementing, Now),
        new StepRunStatusChanged(new RunId("run-1"), new TicketRunId("t-1"), new StepRunId("s-1"), StepStatus.Running, Now),
        new StepRunStatusChanged(new RunId("run-1"), null, new StepRunId("s-2"), StepStatus.Pending, Now),
        new FrontierReconciliationRequested(new RunId("run-1"), Now),
    };

    public void Dispose() => _harness.Dispose();

    [Theory]
    [MemberData(nameof(AllEvents))]
    public async Task Appended_events_are_persisted_with_the_unit_of_work_and_read_back_typed(WorkflowEvent workflowEvent)
    {
        await TestData.SeedTicketRunAsync(_harness);

        using (PersistenceScope writer = _harness.OpenScope())
        {
            EfOutbox outbox = NewOutbox(writer);
            outbox.Append(workflowEvent);
            await writer.SaveAsync();
        }

        using PersistenceScope reader = _harness.OpenScope();
        IReadOnlyList<EventEnvelope> pending = await NewOutbox(reader).ReadPendingAsync(10, CancellationToken.None);

        EventEnvelope envelope = Assert.Single(pending);
        Assert.True(envelope.MessageId > 0);
        Assert.Equal(workflowEvent, envelope.Event);
    }

    [Fact]
    public async Task Progress_events_are_also_written_to_the_run_event_log_in_the_same_save()
    {
        (RunId specRunId, TicketRunId ticketId) = await TestData.SeedTicketRunAsync(_harness);

        using (PersistenceScope writer = _harness.OpenScope())
        {
            EfOutbox outbox = NewOutbox(writer);
            outbox.Append(new SpecRunStatusChanged(specRunId, 1, SpecRunStatus.Queued, SpecRunStatus.Preparing, Now));
            outbox.Append(new TicketRunStatusChanged(specRunId, ticketId, TicketRunStatus.Ready, TicketRunStatus.Implementing, Now));
            outbox.Append(new FrontierReconciliationRequested(specRunId, Now));
            await writer.SaveAsync();
        }

        using PersistenceScope reader = _harness.OpenScope();
        IReadOnlyList<RunEvent> events = await reader.Events.ListBySpecRunAsync(specRunId, CancellationToken.None);
        Assert.Equal([nameof(SpecRunStatusChanged), nameof(TicketRunStatusChanged)], events.Select(e => e.Type));
        Assert.Equal(ticketId, events[1].TicketRunId);
    }

    [Fact]
    public async Task Appended_events_are_not_stored_until_the_unit_of_work_saves()
    {
        using PersistenceScope scope = _harness.OpenScope();
        NewOutbox(scope).Append(new FrontierReconciliationRequested(new RunId("run-1"), Now));

        using PersistenceScope other = _harness.OpenScope();
        Assert.Empty(await NewOutbox(other).ReadPendingAsync(10, CancellationToken.None));
    }

    [Fact]
    public async Task Outbox_row_and_state_change_are_saved_together_and_a_failed_save_stores_neither()
    {
        (int repositoryId, RunId specRunId) = await TestData.SeedSpecRunAsync(_harness);

        using (PersistenceScope loser = _harness.OpenScope())
        using (PersistenceScope winner = _harness.OpenScope())
        {
            SpecRun staleCopy = (await loser.SpecRuns.GetAsync(specRunId, CancellationToken.None))!;
            SpecRun current = (await winner.SpecRuns.GetAsync(specRunId, CancellationToken.None))!;

            current.TransitionTo(SpecRunStatus.Preparing, Now);
            NewOutbox(winner).Append(new SpecRunStatusChanged(specRunId, repositoryId, SpecRunStatus.Queued, SpecRunStatus.Preparing, Now));
            await winner.SaveAsync();

            staleCopy.TransitionTo(SpecRunStatus.Aborted, Now);
            NewOutbox(loser).Append(new SpecRunStatusChanged(specRunId, repositoryId, SpecRunStatus.Queued, SpecRunStatus.Aborted, Now));
            Assert.Equal(SaveOutcome.ConcurrencyConflict, await loser.UnitOfWork.SaveChangesAsync(CancellationToken.None));
        }

        using PersistenceScope reader = _harness.OpenScope();
        EventEnvelope only = Assert.Single(await NewOutbox(reader).ReadPendingAsync(10, CancellationToken.None));
        Assert.Equal(SpecRunStatus.Preparing, ((SpecRunStatusChanged)only.Event).To);
    }

    [Fact]
    public async Task Pending_messages_are_returned_oldest_first_and_limited_by_max_count()
    {
        await AppendAndSaveAsync(Reconciliation("run-a"), Reconciliation("run-b"), Reconciliation("run-c"));

        using PersistenceScope scope = _harness.OpenScope();
        IReadOnlyList<EventEnvelope> pending = await NewOutbox(scope).ReadPendingAsync(2, CancellationToken.None);

        Assert.Equal(["run-a", "run-b"], pending.Select(envelope => ((FrontierReconciliationRequested)envelope.Event).SpecRunId.Value));
    }

    [Fact]
    public async Task Marking_dispatched_removes_the_message_from_pending_and_is_idempotent()
    {
        await AppendAndSaveAsync(Reconciliation("run-a"));
        long id = await FirstPendingIdAsync();

        using (PersistenceScope scope = _harness.OpenScope())
        {
            EfOutbox outbox = NewOutbox(scope);
            await outbox.MarkDispatchedAsync(id, CancellationToken.None);
            _clock.Advance(TimeSpan.FromMinutes(1));
            await outbox.MarkDispatchedAsync(id, CancellationToken.None);
        }

        using PersistenceScope verify = _harness.OpenScope();
        Assert.Empty(await NewOutbox(verify).ReadPendingAsync(10, CancellationToken.None));
        OutboxMessage stored = (await verify.Outbox.GetAsync(id, CancellationToken.None))!;
        Assert.Equal(_clock.UtcNow.AddMinutes(-1), stored.DispatchedAt);
    }

    [Fact]
    public async Task Marking_an_unknown_message_dispatched_is_a_no_op()
    {
        using PersistenceScope scope = _harness.OpenScope();

        await NewOutbox(scope).MarkDispatchedAsync(12345, CancellationToken.None);
    }

    [Fact]
    public async Task Recording_a_failure_keeps_the_message_pending_and_counts_attempts()
    {
        await AppendAndSaveAsync(Reconciliation("run-a"));
        long id = await FirstPendingIdAsync();

        using (PersistenceScope scope = _harness.OpenScope())
        {
            EfOutbox outbox = NewOutbox(scope);
            await outbox.RecordFailureAsync(id, "first", CancellationToken.None);
            await outbox.RecordFailureAsync(id, "second", CancellationToken.None);
        }

        using PersistenceScope verify = _harness.OpenScope();
        Assert.Single(await NewOutbox(verify).ReadPendingAsync(10, CancellationToken.None));
        OutboxMessage stored = (await verify.Outbox.GetAsync(id, CancellationToken.None))!;
        Assert.Equal(2, stored.Attempts);
        Assert.Equal("second", stored.LastError);
    }

    [Fact]
    public async Task An_unreadable_row_is_dead_lettered_without_blocking_valid_messages()
    {
        using (PersistenceScope scope = _harness.OpenScope())
        {
            scope.Outbox.Add(OutboxMessage.Create("RemovedEventType", "{}", Now));
            scope.Outbox.Add(OutboxMessage.Create(nameof(SpecRunStatusChanged), "not json", Now));
            NewOutbox(scope).Append(Reconciliation("run-ok"));
            await scope.SaveAsync();
        }

        using PersistenceScope reader = _harness.OpenScope();
        IReadOnlyList<EventEnvelope> pending = await NewOutbox(reader).ReadPendingAsync(10, CancellationToken.None);

        EventEnvelope valid = Assert.Single(pending);
        Assert.Equal("run-ok", ((FrontierReconciliationRequested)valid.Event).SpecRunId.Value);

        using PersistenceScope verify = _harness.OpenScope();
        OutboxMessage stillPending = Assert.Single(await verify.Outbox.ListPendingAsync(10, CancellationToken.None));
        Assert.Equal(valid.MessageId, stillPending.Id);
        foreach (long poisonId in new long[] { 1, 2 })
        {
            OutboxMessage poison = (await verify.Outbox.GetAsync(poisonId, CancellationToken.None))!;
            Assert.Equal(_clock.UtcNow, poison.DeadLetteredAt);
            Assert.Contains("Unreadable outbox message", poison.LastError);
            Assert.False(poison.IsPending);
        }
    }

    [Fact]
    public async Task Unreadable_rows_beyond_the_batch_size_cannot_starve_delivery_of_valid_messages()
    {
        using (PersistenceScope scope = _harness.OpenScope())
        {
            for (int i = 0; i < 3; i++)
            {
                scope.Outbox.Add(OutboxMessage.Create("RemovedEventType", "{}", Now));
            }

            NewOutbox(scope).Append(Reconciliation("run-ok"));
            await scope.SaveAsync();
        }

        using PersistenceScope reader = _harness.OpenScope();
        IReadOnlyList<EventEnvelope> pending = await NewOutbox(reader).ReadPendingAsync(2, CancellationToken.None);

        EventEnvelope valid = Assert.Single(pending);
        Assert.Equal("run-ok", ((FrontierReconciliationRequested)valid.Event).SpecRunId.Value);
    }

    [Fact]
    public async Task A_message_whose_delivery_keeps_failing_is_dead_lettered_after_the_maximum_attempts()
    {
        await AppendAndSaveAsync(Reconciliation("run-a"));
        long id = await FirstPendingIdAsync();

        using (PersistenceScope scope = _harness.OpenScope())
        {
            EfOutbox outbox = NewOutbox(scope);
            for (int attempt = 1; attempt < EfOutbox.MaxDeliveryAttempts; attempt++)
            {
                await outbox.RecordFailureAsync(id, $"failure {attempt}", CancellationToken.None);
            }
        }

        using (PersistenceScope stillPending = _harness.OpenScope())
        {
            Assert.Single(await NewOutbox(stillPending).ReadPendingAsync(10, CancellationToken.None));
            await NewOutbox(stillPending).RecordFailureAsync(id, "final failure", CancellationToken.None);
        }

        using PersistenceScope verify = _harness.OpenScope();
        Assert.Empty(await NewOutbox(verify).ReadPendingAsync(10, CancellationToken.None));
        OutboxMessage stored = (await verify.Outbox.GetAsync(id, CancellationToken.None))!;
        Assert.Equal(EfOutbox.MaxDeliveryAttempts, stored.Attempts);
        Assert.Equal(_clock.UtcNow, stored.DeadLetteredAt);
        Assert.Equal("final failure", stored.LastError);
    }

    private static FrontierReconciliationRequested Reconciliation(string runId) => new(new RunId(runId), Now);

    private EfOutbox NewOutbox(PersistenceScope scope) => new(scope.Outbox, scope.Events, scope.UnitOfWork, _clock);

    private async Task AppendAndSaveAsync(params WorkflowEvent[] events)
    {
        using PersistenceScope scope = _harness.OpenScope();
        EfOutbox outbox = NewOutbox(scope);
        foreach (WorkflowEvent workflowEvent in events)
        {
            outbox.Append(workflowEvent);
        }

        await scope.SaveAsync();
    }

    private async Task<long> FirstPendingIdAsync()
    {
        using PersistenceScope scope = _harness.OpenScope();
        return (await NewOutbox(scope).ReadPendingAsync(1, CancellationToken.None)).Single().MessageId;
    }
}
