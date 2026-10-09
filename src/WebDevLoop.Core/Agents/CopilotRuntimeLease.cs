namespace WebDevLoop.Core.Agents;

/// <summary>Keeps a runtime alive while a session uses it; disposing lets a draining runtime shut down.</summary>
public sealed class CopilotRuntimeLease(CopilotRuntimeKey key, Func<ValueTask> release) : IAsyncDisposable
{
    private int _released;

    public CopilotRuntimeKey Key { get; } = key;

    public ValueTask DisposeAsync() =>
        Interlocked.Exchange(ref _released, 1) == 0 ? release() : ValueTask.CompletedTask;
}
