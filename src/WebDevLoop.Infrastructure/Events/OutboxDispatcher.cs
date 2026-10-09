using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Recovery.Startup;

namespace WebDevLoop.Infrastructure.Events;

/// <summary>
/// Publishes pending outbox messages to the in-process bus and marks each delivered. Delivery is at-least-once: a crash
/// between publish and mark redelivers the message, which subscribers deduplicate by message id. A failed publish is
/// recorded on the row and leaves it pending for the next pass until <see cref="EfOutbox.MaxDeliveryAttempts"/> is reached, after which the row is dead-lettered. Call from a scope of its own: marking saves that scope's unit of work.
/// </summary>
public sealed class OutboxDispatcher(IOutbox outbox, IRunEventBus bus, OutboxDispatcherOptions options) : IOutboxReplay
{

    /// <summary>One pass over at most <see cref="OutboxDispatcherOptions.BatchSize"/> pending messages. Returns how many were delivered and marked.</summary>
    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<EventEnvelope> pending = await outbox.ReadPendingAsync(options.BatchSize, cancellationToken);
        int dispatched = 0;

        foreach (EventEnvelope envelope in pending)
        {
            try
            {
                await bus.PublishAsync(envelope, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await outbox.RecordFailureAsync(envelope.MessageId, exception.Message, cancellationToken);
                continue;
            }

            await outbox.MarkDispatchedAsync(envelope.MessageId, cancellationToken);
            dispatched++;
        }

        return dispatched;
    }

    /// <summary>
    /// Startup recovery: dispatches batch after batch while every message of a batch is delivered. A pass with a failed
    /// delivery ends the replay; the outbox worker retries those messages once the schedulers run.
    /// </summary>
    public async Task<int> ReplayPendingAsync(CancellationToken cancellationToken)
    {
        int replayed = 0;
        int dispatched;
        do
        {
            dispatched = await DispatchPendingAsync(cancellationToken);
            replayed += dispatched;
        }
        while (dispatched > 0 && dispatched == options.BatchSize);

        return replayed;
    }
}
