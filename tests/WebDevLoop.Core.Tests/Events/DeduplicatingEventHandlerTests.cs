using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;

namespace WebDevLoop.Core.Tests.Events;

public sealed class DeduplicatingEventHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private static EventEnvelope Envelope(long messageId) =>
        new(messageId, new FrontierReconciliationRequested(new RunId("run-1"), Now));

    [Fact]
    public async Task Redelivery_of_the_same_message_id_is_ignored()
    {
        List<long> handled = [];
        var handler = new DeduplicatingEventHandler((envelope, _) =>
        {
            handled.Add(envelope.MessageId);
            return Task.CompletedTask;
        });

        await handler.HandleAsync(Envelope(1), CancellationToken.None);
        await handler.HandleAsync(Envelope(1), CancellationToken.None);
        await handler.HandleAsync(Envelope(2), CancellationToken.None);

        Assert.Equal([1L, 2L], handled);
    }

    [Fact]
    public async Task A_failed_handling_is_not_remembered_so_the_redelivery_runs_again()
    {
        int calls = 0;
        var handler = new DeduplicatingEventHandler((_, _) =>
        {
            calls++;
            return calls == 1 ? Task.FromException(new InvalidOperationException("boom")) : Task.CompletedTask;
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(Envelope(7), CancellationToken.None));
        await handler.HandleAsync(Envelope(7), CancellationToken.None);
        await handler.HandleAsync(Envelope(7), CancellationToken.None);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Oldest_message_ids_are_forgotten_beyond_capacity()
    {
        int calls = 0;
        var handler = new DeduplicatingEventHandler((_, _) =>
        {
            calls++;
            return Task.CompletedTask;
        }, capacity: 2);

        foreach (long id in new long[] { 1, 2, 3 })
        {
            await handler.HandleAsync(Envelope(id), CancellationToken.None);
        }

        await handler.HandleAsync(Envelope(3), CancellationToken.None);
        await handler.HandleAsync(Envelope(1), CancellationToken.None);

        Assert.Equal(4, calls);
    }
}
