using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.Queries;

/// <summary>Per-step subscriber registry. A failing subscriber never affects the others or the caller.</summary>
internal sealed class AgentLogSubscriptions
{
    private readonly object _gate = new();
    private readonly Dictionary<StepRunId, List<Action>> _subscribers = [];

    public IDisposable Subscribe(StepRunId stepRunId, Action onEntriesAvailable)
    {
        lock (_gate)
        {
            if (!_subscribers.TryGetValue(stepRunId, out List<Action>? handlers))
            {
                _subscribers[stepRunId] = handlers = [];
            }

            handlers.Add(onEntriesAvailable);
        }

        return new Subscription(this, stepRunId, onEntriesAvailable);
    }

    public void Notify(IEnumerable<StepRunId> stepRunIds)
    {
        foreach (StepRunId stepRunId in stepRunIds)
        {
            Action[] handlers;
            lock (_gate)
            {
                handlers = _subscribers.TryGetValue(stepRunId, out List<Action>? registered) ? [.. registered] : [];
            }

            foreach (Action handler in handlers)
            {
                try
                {
                    handler();
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // A viewer that cannot be refreshed must not break log persistence or other viewers.
                }
            }
        }
    }

    private void Unsubscribe(StepRunId stepRunId, Action handler)
    {
        lock (_gate)
        {
            if (_subscribers.TryGetValue(stepRunId, out List<Action>? handlers) && handlers.Remove(handler) && handlers.Count == 0)
            {
                _subscribers.Remove(stepRunId);
            }
        }
    }

    private sealed class Subscription(AgentLogSubscriptions owner, StepRunId stepRunId, Action handler) : IDisposable
    {
        public void Dispose() => owner.Unsubscribe(stepRunId, handler);
    }
}
