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
    public async Task app_runtime_receives_the_copilot_only_installation_token_through_its_environment()
    {
        await using CopilotRuntimeHandle handle = await _pool.AcquireAsync(Repo, ShellEnvironment, Ct);

        GitHubTokenRequest request = Assert.Single(_tokens.Requests);
        Assert.Equal(Repo, request.Repo);
        Assert.Equal(GitHubPermissionSet.CopilotRequests, request.Permissions);
        Assert.True(request.AllowUserTokenFallback);
        CopilotRuntimeLaunch launch = Assert.Single(_factory.Runtimes).Launch;
        Assert.Equal("ghs_gen1", launch.Environment["COPILOT_GITHUB_TOKEN"]);
        Assert.Equal("/usr/bin", launch.Environment["PATH"]);
        Assert.Equal("/data/copilot", launch.BaseDirectory);
        Assert.Equal("/opt/copilot/copilot", launch.CliPath);
        Assert.Equal(new CopilotRuntimeKey(new CopilotAuthIdentity(CopilotAuthKind.GitHubAppInstallation, "4242"), 1, _clock.UtcNow + CopilotTokenProviderFake.Lifetime), handle.Lease.Key);
    }

    [Fact]
    public async Task sessions_of_one_identity_share_a_runtime()
    {
        await using CopilotRuntimeHandle first = await _pool.AcquireAsync(Repo, ShellEnvironment, Ct);
        await using CopilotRuntimeHandle second = await _pool.AcquireAsync(new GitHubRepoRef("octo", "other"), ShellEnvironment, Ct);

        Assert.Single(_factory.Runtimes);
        Assert.Same(first.Runtime, second.Runtime);
    }

    [Fact]
    public async Task refreshing_across_app_token_expiry_drains_the_old_runtime_after_its_last_session()
    {
        CopilotRuntimeHandle active = await _pool.AcquireAsync(Repo, ShellEnvironment, Ct);
        CopilotRuntimeKey stale = active.Lease.Key;

        _clock.Advance(TimeSpan.FromMinutes(56));
        IReadOnlyList<CopilotRuntimeKey> replaced = await _pool.RefreshExpiringAsync(Ct);

        Assert.Equal([stale], replaced);
        Assert.Equal(2, _factory.Runtimes.Count);
        Assert.Equal("ghs_gen2", _factory.Runtimes[1].Launch.Environment["COPILOT_GITHUB_TOKEN"]);
        Assert.Equal(_factory.Runtimes[0].Launch.BaseDirectory, _factory.Runtimes[1].Launch.BaseDirectory);
        Assert.False(_factory.Runtimes[0].IsDisposed);

        await active.DisposeAsync();

        Assert.True(_factory.Runtimes[0].IsDisposed);
        await using CopilotRuntimeHandle next = await _pool.AcquireAsync(Repo, ShellEnvironment, Ct);
        Assert.Same(_factory.Runtimes[1], next.Runtime);
        Assert.Equal(2, next.Lease.Key.TokenGeneration);
    }

    [Fact]
    public async Task refresh_leaves_runtimes_that_are_not_close_to_expiry()
    {
        await using CopilotRuntimeHandle handle = await _pool.AcquireAsync(Repo, ShellEnvironment, Ct);

        _clock.Advance(TimeSpan.FromMinutes(30));

        Assert.Empty(await _pool.RefreshExpiringAsync(Ct));
        Assert.Single(_factory.Runtimes);
    }

    [Fact]
    public async Task acquiring_after_token_expiry_starts_a_runtime_with_the_refreshed_token()
    {
        await (await _pool.AcquireAsync(Repo, ShellEnvironment, Ct)).DisposeAsync();

        _clock.Advance(TimeSpan.FromMinutes(58));
        await using CopilotRuntimeHandle handle = await _pool.AcquireAsync(Repo, ShellEnvironment, Ct);

        Assert.Same(_factory.Runtimes[1], handle.Runtime);
        Assert.Equal("ghs_gen2", _factory.Runtimes[1].Launch.Environment["COPILOT_GITHUB_TOKEN"]);
        Assert.True(_factory.Runtimes[0].IsDisposed);
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
            _pool.AcquireAsync(new CopilotAuthIdentity(CopilotAuthKind.GitHubAppInstallation, "unknown"), Ct));
    }

    [Fact]
    public async Task user_token_runtimes_carry_no_token_and_never_expire()
    {
        var tokens = new CopilotTokenProviderFake(_clock, GitHubTokenKind.UserToken);
        await using CopilotRuntimePool pool = CreatePool(tokens);

        await using CopilotRuntimeHandle handle = await pool.AcquireAsync(Repo, ShellEnvironment, Ct);
        _clock.Advance(TimeSpan.FromHours(5));

        Assert.DoesNotContain("COPILOT_GITHUB_TOKEN", _factory.Runtimes[0].Launch.Environment.Keys);
        Assert.Null(handle.Lease.Key.ExpiresAt);
        Assert.Empty(await pool.RefreshExpiringAsync(Ct));
    }

    [Fact]
    public async Task unavailable_copilot_credentials_fail_as_authentication_errors()
    {
        _tokens.UnavailableReason = "The GitHub App is not installed on octo/app.";

        CopilotAuthenticationException exception = await Assert.ThrowsAsync<CopilotAuthenticationException>(() =>
            _pool.AcquireAsync(Repo, ShellEnvironment, Ct));

        Assert.Contains("not installed", exception.Message);
        Assert.Empty(_factory.Runtimes);
    }

    private CopilotRuntimePool CreatePool(ITokenProvider tokens) => new(
        _factory,
        tokens,
        _clock,
        new CopilotRuntimeOptions { BaseDirectory = "/data/copilot", CliPath = "/opt/copilot/copilot", TokenRefreshSkew = TimeSpan.FromMinutes(5) });
}
