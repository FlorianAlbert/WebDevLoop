using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Ports.Fakes;

/// <summary>One runtime per identity; tokens live <c>tokenLifetime</c>, runtimes without leases for <c>idleTimeout</c> are evictable.</summary>
public sealed class FakeCopilotRuntimePool(IClock clock, TimeSpan tokenLifetime, TimeSpan refreshSkew, TimeSpan idleTimeout) : ICopilotRuntimePool
{
    private readonly Dictionary<CopilotAuthIdentity, Runtime> _current = [];

    public Task<CopilotRuntimeLease> AcquireAsync(CopilotAuthIdentity identity, CancellationToken cancellationToken)
    {
        if (!_current.TryGetValue(identity, out Runtime? runtime))
        {
            _current[identity] = runtime = new Runtime(new CopilotRuntimeKey(identity, 1, clock.UtcNow + tokenLifetime), clock.UtcNow);
        }

        runtime.Leases++;
        return Task.FromResult(new CopilotRuntimeLease(runtime.Key, () =>
        {
            runtime.Leases--;
            runtime.IdleSince = clock.UtcNow;
            return ValueTask.CompletedTask;
        }));
    }

    public Task<CopilotRuntimeKey> ReplaceAsync(CopilotRuntimeKey stale, CancellationToken cancellationToken)
    {
        Runtime current = _current[stale.Identity];
        if (current.Key.TokenGeneration > stale.TokenGeneration)
        {
            return Task.FromResult(current.Key);
        }

        var replacement = new CopilotRuntimeKey(stale.Identity, stale.TokenGeneration + 1, clock.UtcNow + tokenLifetime);
        _current[stale.Identity] = new Runtime(replacement, clock.UtcNow);
        return Task.FromResult(replacement);
    }

    public async Task<IReadOnlyList<CopilotRuntimeKey>> RefreshExpiringAsync(CancellationToken cancellationToken)
    {
        CopilotRuntimeKey[] expiring = _current.Values.Select(runtime => runtime.Key).Where(key => key.ExpiresAt <= clock.UtcNow + refreshSkew).ToArray();
        foreach (CopilotRuntimeKey key in expiring)
        {
            await ReplaceAsync(key, cancellationToken);
        }

        return expiring;
    }

    public Task<IReadOnlyList<CopilotRuntimeKey>> EvictIdleAsync(CancellationToken cancellationToken)
    {
        Runtime[] idle = _current.Values.Where(runtime => runtime.Leases == 0 && runtime.IdleSince <= clock.UtcNow - idleTimeout).ToArray();
        foreach (Runtime runtime in idle)
        {
            _current.Remove(runtime.Key.Identity);
        }

        return Task.FromResult<IReadOnlyList<CopilotRuntimeKey>>(idle.Select(runtime => runtime.Key).ToArray());
    }

    private sealed class Runtime(CopilotRuntimeKey key, DateTimeOffset startedAt)
    {
        public CopilotRuntimeKey Key { get; } = key;

        public int Leases { get; set; }

        public DateTimeOffset IdleSince { get; set; } = startedAt;
    }
}
