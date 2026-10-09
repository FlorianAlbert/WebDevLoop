using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Tests.Persistence;

public sealed class CompareAndSwapTests : IDisposable
{
    private const string ReadyTicketId = "t-1";

    // Seeding moves the ticket to Ready with one save, so it starts at version 1.
    private const int SeededVersion = 1;

    private readonly PersistenceHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task two_concurrent_claims_of_the_same_ready_ticket_let_exactly_one_succeed()
    {
        await SeedReadyTicketAsync();
        using PersistenceScope first = _harness.OpenScope();
        using PersistenceScope second = _harness.OpenScope();
        TicketRun firstView = (await first.Tickets.GetAsync(new TicketRunId(ReadyTicketId), CancellationToken.None))!;
        TicketRun secondView = (await second.Tickets.GetAsync(new TicketRunId(ReadyTicketId), CancellationToken.None))!;
        firstView.TransitionTo(TicketRunStatus.Implementing, TestData.Now);
        secondView.TransitionTo(TicketRunStatus.Implementing, TestData.Now);
        first.Steps.Add(TestData.NewStep(firstView.SpecRunId, firstView.Id, "s-first", StepKind.Implement, AgentRole.Implementer));
        second.Steps.Add(TestData.NewStep(secondView.SpecRunId, secondView.Id, "s-second", StepKind.Implement, AgentRole.Implementer));

        SaveOutcome[] outcomes = await Task.WhenAll(
            Task.Run(() => first.UnitOfWork.SaveChangesAsync(CancellationToken.None)),
            Task.Run(() => second.UnitOfWork.SaveChangesAsync(CancellationToken.None)));

        Assert.Single(outcomes, SaveOutcome.Saved);
        Assert.Single(outcomes, SaveOutcome.ConcurrencyConflict);
        using PersistenceScope read = _harness.OpenScope();
        TicketRun stored = (await read.Tickets.GetAsync(new TicketRunId(ReadyTicketId), CancellationToken.None))!;
        Assert.Equal(TicketRunStatus.Implementing, stored.Status);
        Assert.Equal(1, stored.Attempt);
        Assert.Equal(SeededVersion + 1, stored.Version);
        Assert.Single(await read.Steps.ListByTicketRunAsync(stored.Id, CancellationToken.None));
    }

    [Fact]
    public async Task successful_save_advances_the_version_of_modified_aggregates_only()
    {
        await SeedReadyTicketAsync();
        using PersistenceScope scope = _harness.OpenScope();
        TicketRun ticket = (await scope.Tickets.GetAsync(new TicketRunId(ReadyTicketId), CancellationToken.None))!;
        SpecRun untouched = (await scope.SpecRuns.GetAsync(ticket.SpecRunId, CancellationToken.None))!;
        Assert.Equal(SeededVersion, ticket.Version);

        ticket.TransitionTo(TicketRunStatus.Implementing, TestData.Now);
        await scope.SaveAsync();
        Assert.Equal(SeededVersion + 1, ticket.Version);

        ticket.TransitionTo(TicketRunStatus.Reviewing, TestData.Now);
        await scope.SaveAsync();
        Assert.Equal(SeededVersion + 2, ticket.Version);
        Assert.Equal(0, untouched.Version);

        using PersistenceScope read = _harness.OpenScope();
        Assert.Equal(SeededVersion + 2, (await read.Tickets.GetAsync(ticket.Id, CancellationToken.None))!.Version);
        Assert.Equal(0, (await read.SpecRuns.GetAsync(untouched.Id, CancellationToken.None))!.Version);
    }

    [Fact]
    public async Task saving_without_changes_does_not_touch_versions()
    {
        await SeedReadyTicketAsync();
        using PersistenceScope scope = _harness.OpenScope();
        TicketRun ticket = (await scope.Tickets.GetAsync(new TicketRunId(ReadyTicketId), CancellationToken.None))!;

        await scope.SaveAsync();

        Assert.Equal(SeededVersion, ticket.Version);
    }

    [Fact]
    public async Task stale_writer_conflicts_and_saves_nothing_including_added_rows()
    {
        await SeedReadyTicketAsync();
        using PersistenceScope winner = _harness.OpenScope();
        using PersistenceScope stale = _harness.OpenScope();
        TicketRun winnerView = (await winner.Tickets.GetAsync(new TicketRunId(ReadyTicketId), CancellationToken.None))!;
        TicketRun staleView = (await stale.Tickets.GetAsync(new TicketRunId(ReadyTicketId), CancellationToken.None))!;

        winnerView.TransitionTo(TicketRunStatus.Implementing, TestData.Now);
        await winner.SaveAsync();

        staleView.TransitionTo(TicketRunStatus.Skipped, TestData.Now);
        stale.Outbox.Add(OutboxMessage.Create("TicketRunStatusChanged", "{}", TestData.Now));
        stale.Events.Add(RunEvent.Create(staleView.SpecRunId, staleView.Id, "Skipped", "{}", TestData.Now));
        SaveOutcome outcome = await stale.UnitOfWork.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(SaveOutcome.ConcurrencyConflict, outcome);
        Assert.Equal(SeededVersion, staleView.Version);
        using PersistenceScope read = _harness.OpenScope();
        Assert.Equal(TicketRunStatus.Implementing, (await read.Tickets.GetAsync(staleView.Id, CancellationToken.None))!.Status);
        Assert.Empty(await read.Outbox.ListPendingAsync(10, CancellationToken.None));
        Assert.Empty(await read.Events.ListBySpecRunAsync(staleView.SpecRunId, CancellationToken.None));
    }

    [Fact]
    public async Task after_a_conflict_a_fresh_scope_can_retry_the_claim()
    {
        await SeedReadyTicketAsync();
        using PersistenceScope winner = _harness.OpenScope();
        using PersistenceScope loser = _harness.OpenScope();
        TicketRun winnerView = (await winner.Tickets.GetAsync(new TicketRunId(ReadyTicketId), CancellationToken.None))!;
        TicketRun loserView = (await loser.Tickets.GetAsync(new TicketRunId(ReadyTicketId), CancellationToken.None))!;
        winnerView.TransitionTo(TicketRunStatus.Implementing, TestData.Now);
        await winner.SaveAsync();
        loserView.TransitionTo(TicketRunStatus.Implementing, TestData.Now);
        Assert.Equal(SaveOutcome.ConcurrencyConflict, await loser.UnitOfWork.SaveChangesAsync(CancellationToken.None));

        using PersistenceScope retry = _harness.OpenScope();
        TicketRun reloaded = (await retry.Tickets.GetAsync(new TicketRunId(ReadyTicketId), CancellationToken.None))!;
        reloaded.TransitionTo(TicketRunStatus.Reviewing, TestData.Now);
        await retry.SaveAsync();

        Assert.Equal(SeededVersion + 2, reloaded.Version);
    }

    [Fact]
    public async Task outbox_row_is_persisted_atomically_with_the_state_change()
    {
        await SeedReadyTicketAsync();
        using (PersistenceScope scope = _harness.OpenScope())
        {
            TicketRun ticket = (await scope.Tickets.GetAsync(new TicketRunId(ReadyTicketId), CancellationToken.None))!;
            ticket.TransitionTo(TicketRunStatus.Implementing, TestData.Now);
            scope.Outbox.Add(OutboxMessage.Create("TicketRunStatusChanged", "{\"to\":\"Implementing\"}", TestData.Now));
            await scope.SaveAsync();
        }

        using PersistenceScope read = _harness.OpenScope();
        TicketRun stored = (await read.Tickets.GetAsync(new TicketRunId(ReadyTicketId), CancellationToken.None))!;
        OutboxMessage message = Assert.Single(await read.Outbox.ListPendingAsync(10, CancellationToken.None));
        Assert.Equal(TicketRunStatus.Implementing, stored.Status);
        Assert.Equal("{\"to\":\"Implementing\"}", message.PayloadJson);
    }

    [Fact]
    public async Task failed_save_after_a_database_error_leaves_neither_state_change_nor_outbox_row()
    {
        await SeedReadyTicketAsync();
        using PersistenceScope scope = _harness.OpenScope();
        TicketRun ticket = (await scope.Tickets.GetAsync(new TicketRunId(ReadyTicketId), CancellationToken.None))!;
        ticket.TransitionTo(TicketRunStatus.Implementing, TestData.Now);
        scope.Outbox.Add(OutboxMessage.Create("TicketRunStatusChanged", "{}", TestData.Now));
        scope.Events.Add(RunEvent.Create(new RunId("missing-run"), null, "Orphan", "{}", TestData.Now));

        await Assert.ThrowsAnyAsync<Exception>(() => scope.UnitOfWork.SaveChangesAsync(CancellationToken.None));

        using PersistenceScope read = _harness.OpenScope();
        Assert.Equal(TicketRunStatus.Ready, (await read.Tickets.GetAsync(ticket.Id, CancellationToken.None))!.Status);
        Assert.Empty(await read.Outbox.ListPendingAsync(10, CancellationToken.None));
    }

    private async Task SeedReadyTicketAsync()
    {
        (RunId _, TicketRunId ticketId) = await TestData.SeedTicketRunAsync(_harness);
        using PersistenceScope scope = _harness.OpenScope();
        TicketRun ticket = (await scope.Tickets.GetAsync(ticketId, CancellationToken.None))!;
        ticket.TransitionTo(TicketRunStatus.Ready, TestData.Now);
        await scope.SaveAsync();
    }
}
