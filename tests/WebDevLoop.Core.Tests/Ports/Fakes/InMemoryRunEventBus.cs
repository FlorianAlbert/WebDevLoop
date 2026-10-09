using WebDevLoop.Core.Events;

namespace WebDevLoop.Core.Tests.Ports.Fakes;

public sealed class InMemoryRunEventBus : IRunEventBus
{
    private readonly List<Func<EventEnvelope, CancellationToken, Task>> _handlers = [];

    public async Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        foreach (Func<EventEnvelope, CancellationToken, Task> handler in _handlers.ToArray())
        {
            await handler(envelope, cancellationToken);
        }
    }

    public IDisposable Subscribe(Func<EventEnvelope, CancellationToken, Task> handler)
    {
        _handlers.Add(handler);
        return new Subscription(() => _handlers.Remove(handler));
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}
