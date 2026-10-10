using Microsoft.Extensions.Logging.Abstractions;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Recovery.Startup;
using WebDevLoop.Infrastructure.Events;
using WebDevLoop.Infrastructure.Tests.Persistence;

namespace WebDevLoop.Infrastructure.Tests.Events;

public sealed class FrontierReconciliationSignalTests : IDisposable
{
    private readonly PersistenceHarness _harness = new();
    private readonly FixedClock _clock = new(TestData.Now.AddHours(1));

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Raising_the_signal_durably_requests_reconciliation_for_every_non_terminal_run_only()
    {
        (int repositoryId, RunId activeRun) = await TestData.SeedSpecRunAsync(_harness, "run-active");
        using (PersistenceScope scope = _harness.OpenScope())
        {
            SpecRun aborted = TestData.NewSpecRun(repositoryId, "run-aborted", issueNumber: 20, queuePosition: 2);
            aborted.TransitionTo(SpecRunStatus.Aborted, TestData.Now);
            scope.SpecRuns.Add(aborted);
            await scope.SaveAsync();
        }

        int raised;
        using (PersistenceScope scope = _harness.OpenScope())
        {
            raised = await NewSignal(scope).RaiseAsync(CancellationToken.None);
        }

        Assert.Equal(1, raised);
        using PersistenceScope reader = _harness.OpenScope();
        EventEnvelope envelope = Assert.Single(await new EfOutbox(reader.Outbox, reader.Events, reader.UnitOfWork, _clock).ReadPendingAsync(10, CancellationToken.None));
        Assert.Equal(new FrontierReconciliationRequested(activeRun, _clock.UtcNow), envelope.Event);
    }

    [Fact]
    public async Task The_signal_reaches_subscribers_through_the_dispatcher_so_a_missed_event_cannot_stall_work()
    {
        (int _, RunId run) = await TestData.SeedSpecRunAsync(_harness);
        using (PersistenceScope scope = _harness.OpenScope())
        {
            await NewSignal(scope).RaiseAsync(CancellationToken.None);
        }

        var bus = new InProcessRunEventBus(NullLogger<InProcessRunEventBus>.Instance);
        List<RunId> reconciled = [];
        bus.Subscribe((envelope, _) =>
        {
            if (envelope.Event is FrontierReconciliationRequested requested)
            {
                reconciled.Add(requested.SpecRunId);
            }

            return Task.CompletedTask;
        });
        using (PersistenceScope scope = _harness.OpenScope())
        {
            var dispatcher = new OutboxDispatcher(new EfOutbox(scope.Outbox, scope.Events, scope.UnitOfWork, _clock), bus, new OutboxDispatcherOptions());
            await dispatcher.DispatchPendingAsync(CancellationToken.None);
        }

        Assert.Equal([run], reconciled);
    }

    [Fact]
    public async Task Nothing_is_raised_when_no_run_is_active()
    {
        using PersistenceScope scope = _harness.OpenScope();

        Assert.Equal(0, await NewSignal(scope).RaiseAsync(CancellationToken.None));
        Assert.Empty(await scope.Outbox.ListPendingAsync(10, CancellationToken.None));
    }

    private FrontierReconciliationSignal NewSignal(PersistenceScope scope) =>
        new(scope.SpecRuns, new EfOutbox(scope.Outbox, scope.Events, scope.UnitOfWork, _clock), scope.UnitOfWork, _clock);
}
