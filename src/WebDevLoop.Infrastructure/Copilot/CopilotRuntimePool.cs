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
/// Copilot runtimes keyed by auth identity and token generation (and the agent shell environment they hand to tools).
/// App installation tokens can only be given to a runtime through its environment, so a token nearing expiry is rotated
/// by starting a new runtime and draining the old one once its last session lease is released. All runtimes share one
/// base directory, so persisted sessions resume on the replacement. Runtimes without sessions for the idle timeout (e.g.
/// one per tester port) are stopped by <see cref="EvictIdleAsync"/> and restarted on demand.
/// </summary>
internal sealed class CopilotRuntimePool(ICopilotRuntimeFactory factory, ITokenProvider tokens, IClock clock, CopilotRuntimeOptions options)
    : ICopilotRuntimePool, IAsyncDisposable
{
    internal const string CopilotTokenVariable = "COPILOT_GITHUB_TOKEN";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<SlotKey, RuntimeSlot> _current = [];
    private readonly Dictionary<CopilotAuthIdentity, GitHubRepoRef> _tokenSources = [];
    private readonly ConcurrentDictionary<RuntimeSlot, byte> _draining = new();

    /// <summary>Resolves Copilot credentials for <paramref name="repo"/> and leases the matching runtime, starting or rotating it as needed.</summary>
    /// <exception cref="CopilotAuthenticationException">No Copilot credentials are available for the repository.</exception>
    public async Task<CopilotRuntimeHandle> AcquireAsync(
        GitHubRepoRef repo,
        IReadOnlyDictionary<string, string> shellEnvironment,
        CancellationToken cancellationToken)
    {
        GitHubAccessToken token = await RequestTokenAsync(repo, cancellationToken);
        CopilotAuthIdentity identity = token.ToCopilotIdentity();
        var slotKey = new SlotKey(identity, Fingerprint(shellEnvironment));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _tokenSources[identity] = repo;
            if (!_current.TryGetValue(slotKey, out RuntimeSlot? slot) || (IsExpiring(slot.Key) && !slot.Holds(token)))
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
        GitHubRepoRef repo;
        IReadOnlyDictionary<string, string> shellEnvironment;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            RuntimeSlot slot = _current.Values.FirstOrDefault(candidate => candidate.Key.Identity == identity)
                ?? throw new InvalidOperationException($"No Copilot runtime is known for {identity.Kind} '{identity.Id}'; agent sessions start runtimes per repository.");
            repo = _tokenSources[identity];
            shellEnvironment = slot.ShellEnvironment;
        }
        finally
        {
            _gate.Release();
        }

        CopilotRuntimeHandle handle = await AcquireAsync(repo, shellEnvironment, cancellationToken);
        return handle.Lease;
    }

    public async Task<CopilotRuntimeKey> ReplaceAsync(CopilotRuntimeKey stale, CancellationToken cancellationToken)
    {
        CopilotRuntimeKey? replacement = await ReplaceSlotsAsync(stale, requireNewToken: false, cancellationToken);
        if (replacement is not null)
        {
            return replacement;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _current.Values.FirstOrDefault(slot => slot.Key.Identity == stale.Identity)?.Key
                ?? throw new InvalidOperationException($"No Copilot runtime is known for {stale.Identity.Kind} '{stale.Identity.Id}'.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<CopilotRuntimeKey>> RefreshExpiringAsync(CancellationToken cancellationToken)
    {
        CopilotRuntimeKey[] expiring;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            expiring = _current.Values.Select(slot => slot.Key).Where(IsExpiring).Distinct().ToArray();
        }
        finally
        {
            _gate.Release();
        }

        var replaced = new List<CopilotRuntimeKey>();
        foreach (CopilotRuntimeKey stale in expiring)
        {
            if (await ReplaceSlotsAsync(stale, requireNewToken: true, cancellationToken) is not null)
            {
                replaced.Add(stale);
            }
        }

        return replaced;
    }

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

    /// <returns>The replacement key, or null when no runtime had the stale key (or the token has not rotated yet).</returns>
    private async Task<CopilotRuntimeKey?> ReplaceSlotsAsync(CopilotRuntimeKey stale, bool requireNewToken, CancellationToken cancellationToken)
    {
        GitHubRepoRef repo;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_tokenSources.TryGetValue(stale.Identity, out repo))
            {
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }

        GitHubAccessToken token = await RequestTokenAsync(repo, cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            KeyValuePair<SlotKey, RuntimeSlot>[] staleSlots = _current
                .Where(entry => entry.Value.Key == stale && !(requireNewToken && entry.Value.Holds(token)))
                .ToArray();
            CopilotRuntimeKey? replacement = null;
            foreach ((SlotKey slotKey, RuntimeSlot slot) in staleSlots)
            {
                replacement = (await StartSlotAsync(slotKey, slot.ShellEnvironment, token, cancellationToken)).Key;
            }

            return replacement;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Starts a runtime for <paramref name="slotKey"/> and drains the one it replaces. Caller holds the gate.</summary>
    private async Task<RuntimeSlot> StartSlotAsync(
        SlotKey slotKey,
        IReadOnlyDictionary<string, string> shellEnvironment,
        GitHubAccessToken token,
        CancellationToken cancellationToken)
    {
        bool isAppToken = token.Kind == GitHubTokenKind.AppInstallation;
        var key = new CopilotRuntimeKey(slotKey.Identity, token.Generation, isAppToken ? token.ExpiresAt : null);
        var launch = new CopilotRuntimeLaunch(key, options.BaseDirectory, options.CliPath, RuntimeEnvironment(shellEnvironment, token, isAppToken));
        ICopilotRuntime runtime = await factory.StartAsync(launch, cancellationToken);

        var slot = new RuntimeSlot(key, runtime, token, shellEnvironment, clock, retired => _draining.TryRemove(retired, out _));
        if (_current.Remove(slotKey, out RuntimeSlot? previous))
        {
            _draining.TryAdd(previous, 0);
            await previous.RetireAsync();
        }

        _current[slotKey] = slot;
        return slot;
    }

    private static Dictionary<string, string> RuntimeEnvironment(
        IReadOnlyDictionary<string, string> shellEnvironment,
        GitHubAccessToken token,
        bool isAppToken)
    {
        var environment = shellEnvironment
            .Where(variable => !string.Equals(variable.Key, CopilotTokenVariable, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(variable => variable.Key, variable => variable.Value, StringComparer.Ordinal);
        if (isAppToken)
        {
            // Installation tokens must not be passed as an explicit GitHub token; the runtime reads them from its environment.
            environment[CopilotTokenVariable] = token.Value;
        }

        return environment;
    }

    /// <exception cref="CopilotAuthenticationException">No Copilot credentials are available for the repository.</exception>
    internal async Task<GitHubAccessToken> RequestTokenAsync(GitHubRepoRef repo, CancellationToken cancellationToken)
    {
        GitHubTokenResult result = await tokens.GetTokenAsync(
            new GitHubTokenRequest(repo, GitHubPermissionSet.CopilotRequests, AllowUserTokenFallback: true),
            cancellationToken);
        return result.IsAvailable
            ? result.Token
            : throw new CopilotAuthenticationException($"No Copilot credentials are available for {repo}: {result.UnavailableReason}");
    }

    private bool IsExpiring(CopilotRuntimeKey key) => key.ExpiresAt is { } expiresAt && expiresAt - options.TokenRefreshSkew <= clock.UtcNow;

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
        GitHubAccessToken token,
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

        public bool Holds(GitHubAccessToken candidate) =>
            candidate.Generation == token.Generation && string.Equals(candidate.Value, token.Value, StringComparison.Ordinal);

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
