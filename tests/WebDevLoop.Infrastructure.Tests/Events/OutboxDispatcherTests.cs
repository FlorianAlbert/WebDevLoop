using Microsoft.Extensions.Logging.Abstractions;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Infrastructure.Events;
using WebDevLoop.Infrastructure.Tests.Persistence;

namespace WebDevLoop.Infrastructure.Tests.Events;

public sealed class OutboxDispatcherTests : IDisposable
{
    private readonly PersistenceHarness _harness = new();
    private readonly FixedClock _clock = new(TestData.Now);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Pending_messages_are_published_in_order_and_marked_dispatched_once()
    {
        await AppendAsync(Reconciliation("run-a"), Reconciliation("run-b"));
        var bus = new InProcessRunEventBus(NullLogger<InProcessRunEventBus>.Instance);
        List<string> received = [];
        bus.Subscribe((envelope, _) => { received.Add(RunOf(envelope)); return Task.CompletedTask; });

        int first = await DispatchAsync(bus, CancellationToken.None);
        int second = await DispatchAsync(bus, CancellationToken.None);

        Assert.Equal(2, first);
        Assert.Equal(0, second);
        Assert.Equal(["run-a", "run-b"], received);
        Assert.Empty(await ReadPendingAsync());
    }

    [Fact]
    public async Task Pending_rows_are_delivered_after_a_simulated_process_restart()
    {
        await AppendAsync(Reconciliation("run-a"));
        // The first process dies before it ever dispatches; a new process (new scope, bus, subscribers) starts on the same database.
        var restartedBus = new InProcessRunEventBus(NullLogger<InProcessRunEventBus>.Instance);
        List<string> received = [];
        restartedBus.Subscribe((envelope, _) => { received.Add(RunOf(envelope)); return Task.CompletedTask; });

        int dispatched = await DispatchAsync(restartedBus, CancellationToken.None);

        Assert.Equal(1, dispatched);
        Assert.Equal(["run-a"], received);
    }

    [Fact]
    public async Task A_message_published_but_not_marked_before_a_crash_is_redelivered_and_deduplicated_by_subscribers()
    {
        await AppendAsync(Reconciliation("run-a"));
        List<string> handled = [];
        var subscriber = new DeduplicatingEventHandler((envelope, _) => { handled.Add(RunOf(envelope)); return Task.CompletedTask; });
        var bus = new InProcessRunEventBus(NullLogger<InProcessRunEventBus>.Instance);
        bus.Subscribe(subscriber.HandleAsync);

        EventEnvelope delivered = (await ReadPendingAsync()).Single();
        await bus.PublishAsync(delivered, CancellationToken.None); // published, then the process crashed before MarkDispatched
        await DispatchAsync(bus, CancellationToken.None);

        Assert.Equal(["run-a"], handled);
        Assert.Empty(await ReadPendingAsync());
    }

    [Fact]
    public async Task A_bus_failure_records_the_error_keeps_the_message_pending_and_does_not_stop_later_messages()
    {
        await AppendAsync(Reconciliation("run-a"), Reconciliation("run-b"));
        var bus = new FailingOnceBus();

        int dispatched = await DispatchAsync(bus, CancellationToken.None);

        Assert.Equal(1, dispatched);
        EventEnvelope stillPending = Assert.Single(await ReadPendingAsync());
        Assert.Equal("run-a", RunOf(stillPending));
        using PersistenceScope verify = _harness.OpenScope();
        OutboxMessage row = (await verify.Outbox.GetAsync(stillPending.MessageId, CancellationToken.None))!;
        Assert.Equal(1, row.Attempts);
        Assert.Contains("bus down", row.LastError);

        Assert.Equal(1, await DispatchAsync(bus, CancellationToken.None));
        Assert.Empty(await ReadPendingAsync());
    }

    [Fact]
    public async Task Cancellation_stops_dispatching_without_recording_a_failure()
    {
        await AppendAsync(Reconciliation("run-a"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var bus = new InProcessRunEventBus(NullLogger<InProcessRunEventBus>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DispatchAsync(bus, cts.Token));

        using PersistenceScope verify = _harness.OpenScope();
        Assert.Equal(0, (await verify.Outbox.ListPendingAsync(10, CancellationToken.None)).Single().Attempts);
    }

    [Fact]
    public async Task Batch_size_limits_one_pass()
    {
        await AppendAsync(Reconciliation("run-a"), Reconciliation("run-b"), Reconciliation("run-c"));
        var bus = new InProcessRunEventBus(NullLogger<InProcessRunEventBus>.Instance);

        using PersistenceScope scope = _harness.OpenScope();
        var dispatcher = new OutboxDispatcher(new EfOutbox(scope.Outbox, scope.UnitOfWork, _clock), bus, new OutboxDispatcherOptions { BatchSize = 2 });

        Assert.Equal(2, await dispatcher.DispatchPendingAsync(CancellationToken.None));
        Assert.Equal(1, await dispatcher.DispatchPendingAsync(CancellationToken.None));
    }

    private static FrontierReconciliationRequested Reconciliation(string runId) => new(new RunId(runId), TestData.Now);

    private static string RunOf(EventEnvelope envelope) => ((FrontierReconciliationRequested)envelope.Event).SpecRunId.Value;

    private async Task AppendAsync(params WorkflowEvent[] events)
    {
        using PersistenceScope scope = _harness.OpenScope();
        var outbox = new EfOutbox(scope.Outbox, scope.UnitOfWork, _clock);
        foreach (WorkflowEvent workflowEvent in events)
        {
            outbox.Append(workflowEvent);
        }

        await scope.SaveAsync();
    }

    private async Task<IReadOnlyList<EventEnvelope>> ReadPendingAsync()
    {
        using PersistenceScope scope = _harness.OpenScope();
        return await new EfOutbox(scope.Outbox, scope.UnitOfWork, _clock).ReadPendingAsync(100, CancellationToken.None);
    }

    private async Task<int> DispatchAsync(IRunEventBus bus, CancellationToken cancellationToken)
    {
        using PersistenceScope scope = _harness.OpenScope();
        var dispatcher = new OutboxDispatcher(new EfOutbox(scope.Outbox, scope.UnitOfWork, _clock), bus, new OutboxDispatcherOptions());
        return await dispatcher.DispatchPendingAsync(cancellationToken);
    }

    private sealed class FailingOnceBus : IRunEventBus
    {
        private bool _failed;

        public Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
        {
            if (_failed)
            {
                return Task.CompletedTask;
            }

            _failed = true;
            return Task.FromException(new InvalidOperationException("bus down"));
        }

        public IDisposable Subscribe(Func<EventEnvelope, CancellationToken, Task> handler) => throw new NotSupportedException();
    }
}
