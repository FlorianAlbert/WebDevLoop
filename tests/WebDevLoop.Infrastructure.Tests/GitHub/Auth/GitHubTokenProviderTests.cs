using System.Net;
using System.Text.Json;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Auth;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Auth;

public sealed class GitHubTokenProviderTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly GitHubRepoRef Repo = new("acme", "widgets");

    private readonly TestClock _clock = new(Start);
    private readonly FakeGitHubApiHandler _api;

    public GitHubTokenProviderTests()
    {
        _api = new FakeGitHubApiHandler(_clock);
    }

    private GitHubTokenProvider CreateProvider(GitHubAuthOptions? options = null) =>
        new(
            new HttpClient(_api),
            _clock,
            options ?? new GitHubAuthOptions { AppClientId = "Iv1.test", AppPrivateKeyPem = TestRsaKey.PrivateKeyPem });

    private static GitHubTokenRequest Request(GitHubPermissionSet? permissions = null, bool allowFallback = false) =>
        new(Repo, permissions ?? GitHubPermissionSet.ContentsRead, allowFallback);

    [Fact]
    public async Task app_token_is_minted_for_the_repo_installation_with_metadata()
    {
        GitHubTokenResult result = await CreateProvider().GetTokenAsync(Request(), CancellationToken.None);

        Assert.True(result.IsAvailable, result.UnavailableReason);
        Assert.Equal(FakeGitHubApiHandler.TokenValue(1), result.Token.Value);
        Assert.Equal(GitHubTokenKind.AppInstallation, result.Token.Kind);
        Assert.Equal(FakeGitHubApiHandler.InstallationId.ToString(), result.Token.IdentityId);
        Assert.Equal(1, result.Token.Generation);
        Assert.Equal(Start.AddHours(1), result.Token.ExpiresAt);
    }

    [Fact]
    public async Task installation_lookup_and_mint_authenticate_with_an_app_jwt()
    {
        await CreateProvider().GetTokenAsync(Request(), CancellationToken.None);

        Assert.Equal("/repos/acme/widgets/installation", Assert.Single(_api.LookupRequests).Path);
        Assert.All(_api.Requests, request => Assert.StartsWith("Bearer ", request.Authorization));
        Assert.All(_api.Requests, request => Assert.Equal(3, request.Authorization!["Bearer ".Length..].Split('.').Length));
    }

    [Fact]
    public async Task requested_permissions_and_repository_are_serialized_into_the_mint_request()
    {
        GitHubPermissionSet permissions = GitHubPermissionSet.ContentsWrite.With("issues", GitHubPermissionLevel.Read);

        await CreateProvider().GetTokenAsync(Request(permissions), CancellationToken.None);

        using JsonDocument body = JsonDocument.Parse(Assert.Single(_api.MintRequests).Body);
        JsonElement sent = body.RootElement.GetProperty("permissions");
        Assert.Equal("write", sent.GetProperty("contents").GetString());
        Assert.Equal("read", sent.GetProperty("issues").GetString());
        Assert.Equal(2, sent.EnumerateObject().Count());
        Assert.Equal("widgets", Assert.Single(body.RootElement.GetProperty("repositories").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task cached_token_is_reused_before_the_refresh_skew()
    {
        GitHubTokenProvider provider = CreateProvider();
        GitHubTokenResult first = await provider.GetTokenAsync(Request(), CancellationToken.None);

        _clock.Advance(TimeSpan.FromMinutes(54));
        GitHubTokenResult second = await provider.GetTokenAsync(Request(), CancellationToken.None);

        Assert.Equal(first.Token!.Value, second.Token!.Value);
        Assert.Equal(1, _api.MintedTokens);
    }

    [Fact]
    public async Task token_is_refreshed_with_a_new_generation_once_it_expires_within_the_skew()
    {
        GitHubTokenProvider provider = CreateProvider();
        GitHubTokenResult first = await provider.GetTokenAsync(Request(), CancellationToken.None);

        _clock.Advance(TimeSpan.FromMinutes(56));
        GitHubTokenResult second = await provider.GetTokenAsync(Request(), CancellationToken.None);

        Assert.Equal(FakeGitHubApiHandler.TokenValue(2), second.Token!.Value);
        Assert.Equal(first.Token!.Generation + 1, second.Token.Generation);
        Assert.Equal(first.Token.IdentityId, second.Token.IdentityId);
        Assert.Equal(_clock.UtcNow.AddHours(1), second.Token.ExpiresAt);
    }

    [Fact]
    public async Task installation_id_is_looked_up_once_per_repository()
    {
        GitHubTokenProvider provider = CreateProvider();

        await provider.GetTokenAsync(Request(GitHubPermissionSet.ContentsRead), CancellationToken.None);
        await provider.GetTokenAsync(Request(GitHubPermissionSet.IssuesWrite), CancellationToken.None);

        Assert.Single(_api.LookupRequests);
    }

    [Fact]
    public async Task tokens_are_cached_per_permission_set_regardless_of_declaration_order()
    {
        GitHubTokenProvider provider = CreateProvider();
        GitHubPermissionSet issuesThenContents = GitHubPermissionSet.IssuesWrite.With("contents", GitHubPermissionLevel.Read);
        GitHubPermissionSet contentsThenIssues = GitHubPermissionSet.ContentsRead.With("issues", GitHubPermissionLevel.Write);

        GitHubTokenResult readOnly = await provider.GetTokenAsync(Request(GitHubPermissionSet.ContentsRead), CancellationToken.None);
        GitHubTokenResult combined = await provider.GetTokenAsync(Request(issuesThenContents), CancellationToken.None);
        GitHubTokenResult combinedAgain = await provider.GetTokenAsync(Request(contentsThenIssues), CancellationToken.None);

        Assert.NotEqual(readOnly.Token!.Value, combined.Token!.Value);
        Assert.Equal(combined.Token.Value, combinedAgain.Token!.Value);
        Assert.Equal(2, _api.MintedTokens);
    }

    [Fact]
    public async Task concurrent_requests_for_the_same_token_mint_once()
    {
        GitHubTokenProvider provider = CreateProvider();

        GitHubTokenResult[] results = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => provider.GetTokenAsync(Request(), CancellationToken.None)));

        Assert.Equal(1, _api.MintedTokens);
        Assert.All(results, result => Assert.Equal(FakeGitHubApiHandler.TokenValue(1), result.Token!.Value));
    }

    [Fact]
    public async Task generation_is_unique_per_minted_token_of_an_installation()
    {
        GitHubTokenProvider provider = CreateProvider();

        GitHubTokenResult read = await provider.GetTokenAsync(Request(GitHubPermissionSet.ContentsRead), CancellationToken.None);
        GitHubTokenResult write = await provider.GetTokenAsync(Request(GitHubPermissionSet.ContentsWrite), CancellationToken.None);

        Assert.NotEqual(read.Token!.Generation, write.Token!.Generation);
    }

    [Fact]
    public async Task missing_installation_without_fallback_request_is_unavailable_even_if_pat_is_enabled()
    {
        _api.InstallationLookupStatus = HttpStatusCode.NotFound;
        GitHubTokenProvider provider = CreateProvider(OptionsWithPat(patEnabled: true));

        GitHubTokenResult result = await provider.GetTokenAsync(Request(allowFallback: false), CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Contains("not installed", result.UnavailableReason);
    }

    [Fact]
    public async Task pat_fallback_disabled_returns_explicit_unavailable_result()
    {
        _api.InstallationLookupStatus = HttpStatusCode.NotFound;
        GitHubTokenProvider provider = CreateProvider(OptionsWithPat(patEnabled: false));

        GitHubTokenResult result = await provider.GetTokenAsync(Request(allowFallback: true), CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Contains("fallback is disabled", result.UnavailableReason);
        Assert.DoesNotContain("ghp_secret", result.UnavailableReason);
    }

    [Fact]
    public async Task pat_fallback_enabled_and_requested_returns_the_user_token_when_the_app_cannot_act()
    {
        _api.InstallationLookupStatus = HttpStatusCode.NotFound;
        GitHubTokenProvider provider = CreateProvider(OptionsWithPat(patEnabled: true));

        GitHubTokenResult result = await provider.GetTokenAsync(Request(allowFallback: true), CancellationToken.None);

        Assert.True(result.IsAvailable, result.UnavailableReason);
        Assert.Equal("ghp_secret", result.Token.Value);
        Assert.Equal(GitHubTokenKind.UserToken, result.Token.Kind);
        Assert.Null(result.Token.ExpiresAt);
    }

    [Fact]
    public async Task pat_fallback_enabled_but_no_token_configured_is_unavailable()
    {
        _api.InstallationLookupStatus = HttpStatusCode.NotFound;
        var options = new GitHubAuthOptions { AppClientId = "Iv1.test", AppPrivateKeyPem = TestRsaKey.PrivateKeyPem, PatFallbackEnabled = true };

        GitHubTokenResult result = await CreateProvider(options).GetTokenAsync(Request(allowFallback: true), CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Contains("no user token is configured", result.UnavailableReason);
    }

    [Fact]
    public async Task app_token_is_preferred_over_the_pat_when_the_app_can_act()
    {
        GitHubTokenProvider provider = CreateProvider(OptionsWithPat(patEnabled: true));

        GitHubTokenResult result = await provider.GetTokenAsync(Request(allowFallback: true), CancellationToken.None);

        Assert.Equal(GitHubTokenKind.AppInstallation, result.Token!.Kind);
    }

    [Fact]
    public async Task unconfigured_app_uses_the_pat_only_when_enabled_and_requested()
    {
        var options = new GitHubAuthOptions { UserToken = "ghp_secret", PatFallbackEnabled = true };
        GitHubTokenProvider provider = CreateProvider(options);

        GitHubTokenResult notRequested = await provider.GetTokenAsync(Request(allowFallback: false), CancellationToken.None);
        GitHubTokenResult requested = await provider.GetTokenAsync(Request(allowFallback: true), CancellationToken.None);

        Assert.False(notRequested.IsAvailable);
        Assert.Equal("ghp_secret", requested.Token!.Value);
        Assert.Empty(_api.Requests);
    }

    [Fact]
    public async Task mint_rejection_is_unavailable_and_does_not_poison_later_requests()
    {
        GitHubTokenProvider provider = CreateProvider();
        _api.MintStatus = HttpStatusCode.UnprocessableEntity;

        GitHubTokenResult rejected = await provider.GetTokenAsync(Request(), CancellationToken.None);
        _api.MintStatus = HttpStatusCode.Created;
        GitHubTokenResult recovered = await provider.GetTokenAsync(Request(), CancellationToken.None);

        Assert.False(rejected.IsAvailable);
        Assert.Contains("422", rejected.UnavailableReason);
        Assert.True(recovered.IsAvailable);
    }

    [Fact]
    public async Task installation_id_is_looked_up_again_after_the_installation_disappeared()
    {
        GitHubTokenProvider provider = CreateProvider();
        await provider.GetTokenAsync(Request(GitHubPermissionSet.ContentsRead), CancellationToken.None);
        _api.MintStatus = HttpStatusCode.NotFound;

        GitHubTokenResult uninstalled = await provider.GetTokenAsync(Request(GitHubPermissionSet.IssuesWrite), CancellationToken.None);
        _api.MintStatus = HttpStatusCode.Created;
        GitHubTokenResult reinstalled = await provider.GetTokenAsync(Request(GitHubPermissionSet.IssuesWrite), CancellationToken.None);

        Assert.False(uninstalled.IsAvailable);
        Assert.True(reinstalled.IsAvailable);
        Assert.Equal(2, _api.LookupRequests.Count());
    }

    private static GitHubAuthOptions OptionsWithPat(bool patEnabled) => new()
    {
        AppClientId = "Iv1.test",
        AppPrivateKeyPem = TestRsaKey.PrivateKeyPem,
        UserToken = "ghp_secret",
        PatFallbackEnabled = patEnabled,
    };
}
