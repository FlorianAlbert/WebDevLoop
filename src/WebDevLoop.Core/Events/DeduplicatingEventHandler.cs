namespace WebDevLoop.Core.Events;

/// <summary>
/// Makes a handler idempotent for at-least-once delivery: a message id is handled once. The id is claimed before
/// handling (so a concurrent redelivery is ignored) and released if handling fails so a redelivery can retry.
/// Only the most recent <c>capacity</c> ids are remembered.
/// </summary>
public sealed class DeduplicatingEventHandler
{
    private readonly Func<EventEnvelope, CancellationToken, Task> _inner;
    private readonly int _capacity;
    private readonly HashSet<long> _seen = [];
    private readonly Queue<long> _order = new();
    private readonly object _gate = new();

    public DeduplicatingEventHandler(Func<EventEnvelope, CancellationToken, Task> inner, int capacity = 1024)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _inner = inner;
        _capacity = capacity;
    }

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        if (!TryClaim(envelope.MessageId))
        {
            return;
        }

        try
        {
            await _inner(envelope, cancellationToken);
        }
        catch
        {
            Release(envelope.MessageId);
            throw;
        }
    }

    private bool TryClaim(long messageId)
    {
        lock (_gate)
        {
            if (!_seen.Add(messageId))
            {
                return false;
            }

            _order.Enqueue(messageId);
            if (_order.Count > _capacity)
            {
                _seen.Remove(_order.Dequeue());
            }

            return true;
        }
    }

    private void Release(long messageId)
    {
        lock (_gate)
        {
            // Left in the queue: it expires with the capacity window, and re-claiming simply enqueues a second entry.
            _seen.Remove(messageId);
        }
    }
}
