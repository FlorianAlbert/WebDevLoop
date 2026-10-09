using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.Tests.Persistence;

/// <summary>
/// Committed status reads against SQLite: an integration saga keeps one EF Core scope (and identity map) for its whole run,
/// so it must see an abort another scope committed meanwhile instead of its own tracked copies.
/// </summary>
public sealed class CommittedStatusTests : IDisposable
{
    private readonly PersistenceHarness _harness = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task a_long_lived_scope_reads_the_status_another_scope_committed_without_tracking()
    {
        (RunId specRunId, TicketRunId ticketRunId) = await TestData.SeedTicketRunAsync(_harness);
        using PersistenceScope saga = _harness.OpenScope();
        await saga.SpecRuns.GetAsync(specRunId, Token);
        await saga.Tickets.GetAsync(ticketRunId, Token);
        int tracked = saga.Context.ChangeTracker.Entries().Count();

        using (PersistenceScope abort = _harness.OpenScope())
        {
            (await abort.SpecRuns.GetAsync(specRunId, Token))!.TransitionTo(SpecRunStatus.Aborted, TestData.Now);
            (await abort.Tickets.GetAsync(ticketRunId, Token))!.TransitionTo(TicketRunStatus.Aborted, TestData.Now);
            await abort.SaveAsync();
        }

        Assert.Equal(SpecRunStatus.Aborted, await saga.SpecRuns.GetCommittedStatusAsync(specRunId, Token));
        Assert.Equal(TicketRunStatus.Aborted, await saga.Tickets.GetCommittedStatusAsync(ticketRunId, Token));
        Assert.Null(await saga.SpecRuns.GetCommittedStatusAsync(new RunId("missing"), Token));
        Assert.Null(await saga.Tickets.GetCommittedStatusAsync(new TicketRunId("missing"), Token));
        Assert.Equal(tracked, saga.Context.ChangeTracker.Entries().Count());
    }
}
