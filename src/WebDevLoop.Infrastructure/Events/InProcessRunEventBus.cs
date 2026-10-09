using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using WebDevLoop.Core.Events;

namespace WebDevLoop.Infrastructure.Events;

/// <summary>
/// Delivers each envelope to every current subscriber in subscription order. A failing subscriber is logged and never
/// prevents delivery to the others; its work is recovered by frontier reconciliation. Cancellation is not swallowed.
/// </summary>
public sealed partial class InProcessRunEventBus(ILogger<InProcessRunEventBus> logger) : IRunEventBus
{
    private readonly object _gate = new();
    private ImmutableList<Func<EventEnvelope, CancellationToken, Task>> _handlers = [];

    public async Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        foreach (Func<EventEnvelope, CancellationToken, Task> handler in _handlers)
        {
            try
            {
                await handler(envelope, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogSubscriberFailed(logger, exception, envelope.MessageId, envelope.Event.GetType().Name);
            }
        }
    }

    public IDisposable Subscribe(Func<EventEnvelope, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            _handlers = _handlers.Add(handler);
        }

        return new Subscription(() =>
        {
            lock (_gate)
            {
                _handlers = _handlers.Remove(handler);
            }
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Event subscriber failed for outbox message {MessageId} ({EventType}); other subscribers were still notified.")]
    private static partial void LogSubscriberFailed(ILogger logger, Exception exception, long messageId, string eventType);

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}
