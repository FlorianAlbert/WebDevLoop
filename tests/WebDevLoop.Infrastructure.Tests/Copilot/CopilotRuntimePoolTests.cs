using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Copilot;
using WebDevLoop.Infrastructure.Copilot.Runtime;
using WebDevLoop.Infrastructure.Tests.Copilot.Fakes;
using WebDevLoop.Infrastructure.Tests.GitHub.Auth;

namespace WebDevLoop.Infrastructure.Tests.Copilot;

public sealed class CopilotRuntimePoolTests : IAsyncDisposable
{
    private static readonly GitHubRepoRef Repo = new("octo", "app");
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);
    private static readonly IReadOnlyDictionary<string, string> ShellEnvironment = new Dictionary<string, string>
    {
        ["PATH"] = "/usr/bin",
        ["GIT_TERMINAL_PROMPT"] = "0",
    };

    private readonly TestClock _clock = new(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeCopilotRuntimeFactory _factory = new();
    private readonly CopilotTokenProviderFake _tokens;
    private readonly CopilotRuntimePool _pool;

    public CopilotRuntimePoolTests()
    {
        _tokens = new CopilotTokenProviderFake(_clock);
        _pool = CreatePool(_tokens);
    }

    public ValueTask DisposeAsync() => _pool.DisposeAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task runtime_starts_without_any_github_token_in_its_environment()
    {
        var environment = new Dictionary<string, string>(ShellEnvironment) { ["COPILOT_GITHUB_TOKEN"] = "ghp_from_the_host" };

        await using CopilotRuntimeHandle handle = await _pool.AcquireAsync(Repo, environment, Ct);

        Assert.Equal(1, _tokens.Requests);
        CopilotRuntimeLaunch launch = Assert.Single(_factory.Runtimes).Launch;
        Assert.DoesNotContain("COPILOT_GITHUB_TOKEN", launch.Environment.Keys);
        Assert.Equal("/usr/bin", launch.Environment["PATH"]);
        Assert.Equal("/data/copilot", launch.BaseDirectory);
        Assert.Equal("/opt/copilot/copilot", launch.CliPath);
        Assert.Equal(new CopilotRuntimeKey(new CopilotAuthIdentity(CopilotTokenProviderFake.Login), 1, null), handle.Lease.Key);
        Assert.Equal("ghu_gen1", handle.Token.Value);
    }

    [Fact]
    public async Task sessions_of_one_user_share_a_runtime()
    {
        await using CopilotRuntimeHandle first = await _pool.AcquireAsync(Repo, ShellEnvironment, Ct);
        await using CopilotRuntimeHandle second = await _pool.AcquireAsync(new GitHubRepoRef("octo", "other"), ShellEnvironment, Ct);

        Assert.Single(_factory.Runtimes);
        Assert.Same(first.Runtime, second.Runtime);
    }

    [Fact]
    public async Task a_refreshed_user_token_keeps_the_runtime_and_is_handed_to_new_sessions()
    {
        await using CopilotRuntimeHandle first = await _pool.AcquireAsync(Repo, ShellEnvironment, Ct);

        _clock.Advance(CopilotTokenProviderFake.Lifetime);
        await using CopilotRuntimeHandle second = await _pool.AcquireAsync(Repo, ShellEnvironment, Ct);

        Assert.Single(_factory.Runtimes);
        Assert.Same(first.Runtime, second.Runtime);
        Assert.Equal("ghu_gen2", second.Token.Value);
        Assert.Empty(await _pool.RefreshExpiringAsync(Ct));
    }

    [Fact]
    public async Task replace_restarts_the_runtime_even_with_an_unchanged_token()
    {
        CopilotRuntimeKey stale;
        await using (CopilotRuntimeHandle handle = await _pool.AcquireAsync(Repo, ShellEnvironment, Ct))
        {
            stale = handle.Lease.Key;
        }

        CopilotRuntimeKey replacement = await _pool.ReplaceAsync(stale, Ct);

        Assert.Equal(2, _factory.Runtimes.Count);
        Assert.True(_factory.Runtimes[0].IsDisposed);
        Assert.Equal(stale.Identity, replacement.Identity);
        await using CopilotRuntimeHandle next = await _pool.AcquireAsync(Repo, ShellEnvironment, Ct);
        Assert.Same(_factory.Runtimes[1], next.Runtime);
    }

    [Fact]
    public async Task port_acquire_leases_the_current_runtime_of_a_known_identity()
    {
        CopilotAuthIdentity identity;
        await using (CopilotRuntimeHandle handle = await _pool.AcquireAsync(Repo, ShellEnvironment, Ct))
        {
            identity = handle.Lease.Key.Identity;
        }

        await using CopilotRuntimeLease lease = await _pool.AcquireAsync(identity, Ct);

        Assert.Equal(identity, lease.Key.Identity);
        Assert.Single(_factory.Runtimes);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _pool.AcquireAsync(new CopilotAuthIdentity("unknown"), Ct));
    }

    [Fact]
    public async Task runtimes_idle_for_the_idle_timeout_are_evicted_and_restarted_on_demand()
    {
        var testerEnvironment = new Dictionary<string, string>(ShellEnvironment) { ["WEBDEVLOOP_TEST_PORT"] = "41003" };
        await (await _pool.AcquireAsync(Repo, testerEnvironment, Ct)).DisposeAsync();
        await using CopilotRuntimeHandle busy = await _pool.AcquireAsync(Repo, ShellEnvironment, Ct);
        _clock.Advance(IdleTimeout);

        IReadOnlyList<CopilotRuntimeKey> evicted = await _pool.EvictIdleAsync(Ct);

        Assert.Equal([_factory.Runtimes[0].Launch.Key], evicted);
        Assert.True(_factory.Runtimes[0].IsDisposed);
        Assert.False(_factory.Runtimes[1].IsDisposed);
        await using CopilotRuntimeHandle again = await _pool.AcquireAsync(Repo, testerEnvironment, Ct);
        Assert.Same(_factory.Runtimes[2], again.Runtime);
    }

    [Fact]
    public async Task idle_time_counts_from_the_last_released_session()
    {
        CopilotRuntimeHandle longSession = await _pool.AcquireAsync(Repo, ShellEnvironment, Ct);
        _clock.Advance(IdleTimeout * 2);
        await longSession.DisposeAsync();
        _clock.Advance(IdleTimeout - TimeSpan.FromSeconds(1));

        Assert.Empty(await _pool.EvictIdleAsync(Ct));
        Assert.False(_factory.Runtimes[0].IsDisposed);
    }

    [Fact]
    public async Task unavailable_copilot_credentials_fail_as_authentication_errors()
    {
        _tokens.UnavailableReason = "Nobody is signed in to GitHub.";

        CopilotAuthenticationException exception = await Assert.ThrowsAsync<CopilotAuthenticationException>(() =>
            _pool.AcquireAsync(Repo, ShellEnvironment, Ct));

        Assert.Contains("octo/app: Nobody is signed in to GitHub.", exception.Message);
        Assert.Empty(_factory.Runtimes);
    }

    private CopilotRuntimePool CreatePool(ITokenProvider tokens) => new(
        _factory,
        tokens,
        _clock,
        new CopilotRuntimeOptions
        {
            BaseDirectory = "/data/copilot",
            CliPath = "/opt/copilot/copilot",
            IdleTimeout = IdleTimeout,
        });
}
