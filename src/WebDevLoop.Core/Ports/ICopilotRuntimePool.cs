using WebDevLoop.Core.Agents;

namespace WebDevLoop.Core.Ports;

/// <summary>Copilot runtimes keyed by auth identity and token generation.</summary>
public interface ICopilotRuntimePool
{
    /// <summary>Leases the current runtime for <paramref name="identity"/>, starting one if needed.</summary>
    Task<CopilotRuntimeLease> AcquireAsync(CopilotAuthIdentity identity, CancellationToken cancellationToken);

    /// <summary>Starts a runtime with a fresh token generation and drains <paramref name="stale"/> (e.g. after an auth failure).</summary>
    Task<CopilotRuntimeKey> ReplaceAsync(CopilotRuntimeKey stale, CancellationToken cancellationToken);

    /// <summary>Replaces every runtime whose token expires within the refresh skew; returns the replaced keys.</summary>
    Task<IReadOnlyList<CopilotRuntimeKey>> RefreshExpiringAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stops runtimes no session has leased for the idle timeout (e.g. the per-port runtime of a finished tester); the next
    /// session needing one starts it again. Returns the stopped keys.
    /// </summary>
    Task<IReadOnlyList<CopilotRuntimeKey>> EvictIdleAsync(CancellationToken cancellationToken);
}
