using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Copilot.Runtime;
using WebDevLoop.Infrastructure.GitHub.Auth;

namespace WebDevLoop.Infrastructure.Copilot;

/// <summary>
/// Copilot runtimes keyed by the signed-in user and the agent shell environment they hand to tools. Runtimes never hold a
/// GitHub token: sessions receive the user's token (and its refreshes, through a callback), so a refreshed token never
/// needs a new runtime. All runtimes share one base directory, so persisted sessions resume on a replacement. Runtimes
/// without sessions for the idle timeout (e.g. one per tester port) are stopped by <see cref="EvictIdleAsync"/> and
/// restarted on demand.
/// </summary>
internal sealed class CopilotRuntimePool(ICopilotRuntimeFactory factory, ITokenProvider tokens, IClock clock, CopilotRuntimeOptions options)
    : ICopilotRuntimePool, IAsyncDisposable
{
    /// <summary>Removed from the runtime environment so the runtime never authenticates with a token of the host process.</summary>
    internal const string CopilotTokenVariable = "COPILOT_GITHUB_TOKEN";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<SlotKey, RuntimeSlot> _current = [];
    private readonly ConcurrentDictionary<RuntimeSlot, byte> _draining = new();

    /// <summary>Resolves the signed-in user's token and leases the matching runtime, starting it as needed.</summary>
    /// <param name="repo">The repository the session works on (named in authentication errors).</param>
    /// <exception cref="CopilotAuthenticationException">Nobody is signed in to GitHub.</exception>
    public async Task<CopilotRuntimeHandle> AcquireAsync(
        GitHubRepoRef repo,
        IReadOnlyDictionary<string, string> shellEnvironment,
        CancellationToken cancellationToken)
    {
        GitHubAccessToken token = await RequestTokenAsync(repo, cancellationToken);
        var slotKey = new SlotKey(token.ToCopilotIdentity(), Fingerprint(shellEnvironment));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_current.TryGetValue(slotKey, out RuntimeSlot? slot))
            {
                slot = await StartSlotAsync(slotKey, shellEnvironment, token, cancellationToken);
            }

            return new CopilotRuntimeHandle(slot.Lease(), slot.Runtime, token);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CopilotRuntimeLease> AcquireAsync(CopilotAuthIdentity identity, CancellationToken cancellationToken)
    {
        RuntimeSlot slot;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            slot = _current.Values.FirstOrDefault(candidate => candidate.Key.Identity == identity)
                ?? throw new InvalidOperationException($"No Copilot runtime is known for '{identity.Id}'; agent sessions start runtimes per repository.");
            return slot.Lease();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CopilotRuntimeKey> ReplaceAsync(CopilotRuntimeKey stale, CancellationToken cancellationToken)
    {
        GitHubAccessToken token = await RequestTokenAsync(null, cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            KeyValuePair<SlotKey, RuntimeSlot>[] staleSlots = _current.Where(entry => entry.Value.Key == stale).ToArray();
            CopilotRuntimeKey? replacement = null;
            foreach ((SlotKey slotKey, RuntimeSlot slot) in staleSlots)
            {
                replacement = (await StartSlotAsync(slotKey, slot.ShellEnvironment, token, cancellationToken)).Key;
            }

            return replacement
                ?? _current.Values.FirstOrDefault(slot => slot.Key.Identity == stale.Identity)?.Key
                ?? throw new InvalidOperationException($"No Copilot runtime is known for '{stale.Identity.Id}'.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Runtimes never hold a token (sessions get the refreshed user token through a callback), so none expires.</summary>
    public Task<IReadOnlyList<CopilotRuntimeKey>> RefreshExpiringAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CopilotRuntimeKey>>([]);

    public async Task<IReadOnlyList<CopilotRuntimeKey>> EvictIdleAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset idleSince = clock.UtcNow - options.IdleTimeout;
        KeyValuePair<SlotKey, RuntimeSlot>[] idle;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Leases are only handed out under the gate, so a slot found idle here cannot gain a session before it is removed.
            idle = _current.Where(entry => entry.Value.IsIdleSince(idleSince)).ToArray();
            foreach ((SlotKey slotKey, _) in idle)
            {
                _current.Remove(slotKey);
            }
        }
        finally
        {
            _gate.Release();
        }

        foreach ((_, RuntimeSlot slot) in idle)
        {
            await slot.RetireAsync();
        }

        return idle.Select(entry => entry.Value.Key).ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        RuntimeSlot[] slots;
        await _gate.WaitAsync();
        try
        {
            slots = [.. _current.Values, .. _draining.Keys];
            _current.Clear();
        }
        finally
        {
            _gate.Release();
        }

        foreach (RuntimeSlot slot in slots)
        {
            await slot.DisposeRuntimeAsync();
        }
    }

    /// <summary>Starts a runtime for <paramref name="slotKey"/> and drains the one it replaces. Caller holds the gate.</summary>
    private async Task<RuntimeSlot> StartSlotAsync(
        SlotKey slotKey,
        IReadOnlyDictionary<string, string> shellEnvironment,
        GitHubAccessToken token,
        CancellationToken cancellationToken)
    {
        var key = new CopilotRuntimeKey(slotKey.Identity, token.Generation, null);
        var launch = new CopilotRuntimeLaunch(key, options.BaseDirectory, options.CliPath, RuntimeEnvironment(shellEnvironment));
        ICopilotRuntime runtime = await factory.StartAsync(launch, cancellationToken);

        var slot = new RuntimeSlot(key, runtime, shellEnvironment, clock, retired => _draining.TryRemove(retired, out _));
        if (_current.Remove(slotKey, out RuntimeSlot? previous))
        {
            _draining.TryAdd(previous, 0);
            await previous.RetireAsync();
        }

        _current[slotKey] = slot;
        return slot;
    }

    private static Dictionary<string, string> RuntimeEnvironment(IReadOnlyDictionary<string, string> shellEnvironment) =>
        shellEnvironment
            .Where(variable => !string.Equals(variable.Key, CopilotTokenVariable, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(variable => variable.Key, variable => variable.Value, StringComparer.Ordinal);

    /// <exception cref="CopilotAuthenticationException">Nobody is signed in to GitHub.</exception>
    internal async Task<GitHubAccessToken> RequestTokenAsync(GitHubRepoRef? repo, CancellationToken cancellationToken)
    {
        GitHubTokenResult result = await tokens.GetTokenAsync(cancellationToken);
        return result.IsAvailable
            ? result.Token
            : throw new CopilotAuthenticationException(repo is null
                ? $"No Copilot credentials are available: {result.UnavailableReason}"
                : $"No Copilot credentials are available for {repo}: {result.UnavailableReason}");
    }

    private static string Fingerprint(IReadOnlyDictionary<string, string> environment)
    {
        var canonical = new StringBuilder();
        foreach ((string name, string value) in environment.OrderBy(variable => variable.Key, StringComparer.Ordinal))
        {
            canonical.Append(name).Append('=').Append(value).Append('\0');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private readonly record struct SlotKey(CopilotAuthIdentity Identity, string EnvironmentFingerprint);

    private sealed class RuntimeSlot(
        CopilotRuntimeKey key,
        ICopilotRuntime runtime,
        IReadOnlyDictionary<string, string> shellEnvironment,
        IClock clock,
        Action<RuntimeSlot> disposed)
    {
        private int _leases;
        private int _retired;
        private int _disposed;
        private long _idleSinceUtcTicks = clock.UtcNow.UtcTicks;

        public CopilotRuntimeKey Key { get; } = key;

        public ICopilotRuntime Runtime { get; } = runtime;

        public IReadOnlyDictionary<string, string> ShellEnvironment { get; } = shellEnvironment;

        /// <summary>No session holds a lease, and none was released after <paramref name="cutoff"/>.</summary>
        public bool IsIdleSince(DateTimeOffset cutoff) =>
            Volatile.Read(ref _leases) == 0 && Interlocked.Read(ref _idleSinceUtcTicks) <= cutoff.UtcTicks;

        public CopilotRuntimeLease Lease()
        {
            Interlocked.Increment(ref _leases);
            return new CopilotRuntimeLease(Key, ReleaseAsync);
        }

        /// <summary>Stops handing out leases; the runtime stops once the last session released it.</summary>
        public ValueTask RetireAsync()
        {
            Volatile.Write(ref _retired, 1);
            return Volatile.Read(ref _leases) == 0 ? DisposeRuntimeAsync() : ValueTask.CompletedTask;
        }

        public ValueTask DisposeRuntimeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return ValueTask.CompletedTask;
            }

            disposed(this);
            return Runtime.DisposeAsync();
        }

        private ValueTask ReleaseAsync()
        {
            if (Interlocked.Decrement(ref _leases) != 0)
            {
                return ValueTask.CompletedTask;
            }

            Interlocked.Exchange(ref _idleSinceUtcTicks, clock.UtcNow.UtcTicks);
            return Volatile.Read(ref _retired) == 1 ? DisposeRuntimeAsync() : ValueTask.CompletedTask;
        }
    }
}
