using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Ports.Fakes;

public sealed class FakeCopilotRuntimePool(IClock clock, TimeSpan tokenLifetime, TimeSpan refreshSkew) : ICopilotRuntimePool
{
    private readonly Dictionary<CopilotAuthIdentity, CopilotRuntimeKey> _current = [];

    public int ActiveLeases { get; private set; }

    public Task<CopilotRuntimeLease> AcquireAsync(CopilotAuthIdentity identity, CancellationToken cancellationToken)
    {
        if (!_current.TryGetValue(identity, out CopilotRuntimeKey? key))
        {
            _current[identity] = key = new CopilotRuntimeKey(identity, 1, clock.UtcNow + tokenLifetime);
        }

        ActiveLeases++;
        return Task.FromResult(new CopilotRuntimeLease(key, () =>
        {
            ActiveLeases--;
            return ValueTask.CompletedTask;
        }));
    }

    public Task<CopilotRuntimeKey> ReplaceAsync(CopilotRuntimeKey stale, CancellationToken cancellationToken)
    {
        CopilotRuntimeKey current = _current[stale.Identity];
        if (current.TokenGeneration > stale.TokenGeneration)
        {
            return Task.FromResult(current);
        }

        var replacement = new CopilotRuntimeKey(stale.Identity, stale.TokenGeneration + 1, clock.UtcNow + tokenLifetime);
        _current[stale.Identity] = replacement;
        return Task.FromResult(replacement);
    }

    public async Task<IReadOnlyList<CopilotRuntimeKey>> RefreshExpiringAsync(CancellationToken cancellationToken)
    {
        CopilotRuntimeKey[] expiring = _current.Values.Where(key => key.ExpiresAt <= clock.UtcNow + refreshSkew).ToArray();
        foreach (CopilotRuntimeKey key in expiring)
        {
            await ReplaceAsync(key, cancellationToken);
        }

        return expiring;
    }
}
