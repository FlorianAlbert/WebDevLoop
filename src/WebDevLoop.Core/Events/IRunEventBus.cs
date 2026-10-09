namespace WebDevLoop.Core.Events;

/// <summary>In-process fan-out of dispatched outbox events. Delivery is at-least-once.</summary>
public interface IRunEventBus
{
    Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken);

    /// <summary>Dispose the returned handle to unsubscribe.</summary>
    IDisposable Subscribe(Func<EventEnvelope, CancellationToken, Task> handler);
}
