using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Auth;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Auth;

public sealed class GitHubUserSessionTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri RedirectUri = new("http://localhost:5240/auth/github/callback");

    private readonly TestClock _clock = new(Start);
    private readonly FakeGitHubOAuthHandler _github = new();
    private readonly InMemoryCredentialStore _store = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void authorization_uri_targets_the_app_with_state_and_a_pkce_challenge()
    {
        Uri uri = CreateSession().CreateAuthorizationUri(RedirectUri, "state-1", "challenge-1");

        Assert.Equal("https://github.com/login/oauth/authorize", uri.GetLeftPart(UriPartial.Path));
        Assert.Contains("client_id=Iv23.client", uri.Query);
        Assert.Contains($"redirect_uri={Uri.EscapeDataString(RedirectUri.ToString())}", uri.Query);
        Assert.Contains("state=state-1", uri.Query);
        Assert.Contains("code_challenge=challenge-1", uri.Query);
        Assert.Contains("code_challenge_method=S256", uri.Query);
        Assert.DoesNotContain("client-secret", uri.ToString());
    }

    [Fact]
    public async Task completing_the_sign_in_exchanges_the_code_stores_the_tokens_and_signals_the_change()
    {
        _github.EnqueueToken(FakeGitHubOAuthHandler.Tokens("ghu_first"));
        GitHubUserSession session = CreateSession();
        int changes = 0;
        session.Changed += () => changes++;

        GitHubSignInStatus status = await session.CompleteSignInAsync("code-1", RedirectUri, "verifier-1", Ct);

        IReadOnlyDictionary<string, string> form = Assert.Single(_github.TokenRequests).Form;
        Assert.Equal("Iv23.client", form["client_id"]);
        Assert.Equal("client-secret", form["client_secret"]);
        Assert.Equal("code-1", form["code"]);
        Assert.Equal("verifier-1", form["code_verifier"]);
        Assert.Equal(RedirectUri.ToString(), form["redirect_uri"]);
        Assert.Equal("Bearer ghu_first", Assert.Single(_github.Requests, request => request.Uri.AbsolutePath == "/user").Authorization);
        Assert.Equal(("octocat", "https://avatars.example/octocat"), (status.Login, status.AvatarUrl));
        Assert.Equal(Start.AddSeconds(15897600), status.SignInExpiresAt);
        Assert.Equal(1, changes);
        Assert.Equal("ghu_first", _store.Stored!.AccessToken);
        Assert.Equal(Start.AddHours(8), _store.Stored.AccessTokenExpiresAt);
        Assert.Equal("ghr_refresh", _store.Stored.RefreshToken);
        Assert.DoesNotContain("ghu_first", _store.Stored.ToString());

        GitHubTokenResult token = await session.GetTokenAsync(Ct);
        Assert.Equal("ghu_first", token.Token!.Value);
        Assert.Equal("octocat", token.Token.IdentityId);
        Assert.Equal(Start.AddHours(8), token.Token.ExpiresAt);
    }

    [Fact]
    public async Task a_rejected_code_fails_the_sign_in_without_storing_anything()
    {
        _github.EnqueueToken(FakeGitHubOAuthHandler.Error("bad_verification_code", "The code passed is incorrect or expired."));
        GitHubUserSession session = CreateSession();

        GitHubSignInException exception = await Assert.ThrowsAsync<GitHubSignInException>(
            () => session.CompleteSignInAsync("stale", RedirectUri, "verifier", Ct));

        Assert.Contains("The code passed is incorrect or expired.", exception.Message);
        Assert.Null(_store.Stored);
        Assert.False(session.Status.IsSignedIn);
    }

    [Fact]
    public async Task an_unreachable_github_fails_the_sign_in_with_a_sign_in_exception()
    {
        _github.TokenEndpointUnreachable = true;

        await Assert.ThrowsAsync<GitHubSignInException>(() => CreateSession().CompleteSignInAsync("code", RedirectUri, "verifier", Ct));
    }

    [Fact]
    public async Task nobody_signed_in_yields_an_unavailable_token_asking_to_sign_in()
    {
        GitHubTokenResult result = await CreateSession().GetTokenAsync(Ct);

        Assert.False(result.IsAvailable);
        Assert.Contains("sign in with GitHub", result.UnavailableReason);
        Assert.Empty(_github.Requests);
    }

    [Fact]
    public async Task a_stored_sign_in_is_used_after_a_restart_without_contacting_github()
    {
        _store.Stored = Credentials("ghu_stored", Start.AddHours(4));

        GitHubUserSession session = CreateSession();
        GitHubTokenResult result = await session.GetTokenAsync(Ct);

        Assert.Equal("octocat", session.Status.Login);
        Assert.Equal("ghu_stored", result.Token!.Value);
        Assert.Empty(_github.Requests);
    }

    [Fact]
    public async Task an_expiring_token_is_refreshed_once_with_a_new_generation_and_the_rotated_refresh_token_is_stored()
    {
        _store.Stored = Credentials("ghu_old", Start.AddMinutes(4));
        _github.EnqueueToken(FakeGitHubOAuthHandler.Tokens("ghu_new", refresh: "ghr_rotated"));
        GitHubUserSession session = CreateSession();

        GitHubTokenResult[] results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => session.GetTokenAsync(Ct)));

        IReadOnlyDictionary<string, string> form = Assert.Single(_github.TokenRequests).Form;
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("ghr_stored", form["refresh_token"]);
        Assert.All(results, result => Assert.Equal("ghu_new", result.Token!.Value));
        Assert.All(results, result => Assert.Equal(2, result.Token!.Generation));
        Assert.Equal("ghr_rotated", _store.Stored!.RefreshToken);
        Assert.Equal(Start.AddHours(8), _store.Stored.AccessTokenExpiresAt);
        Assert.Equal("octocat", _store.Stored.Login);
    }

    [Fact]
    public async Task a_refresh_is_not_abandoned_when_the_caller_gives_up_because_the_used_refresh_token_cannot_be_replayed()
    {
        _store.Stored = Credentials("ghu_old", Start.AddMinutes(1));
        _github.EnqueueToken(FakeGitHubOAuthHandler.Tokens("ghu_new", refresh: "ghr_rotated"));
        GitHubUserSession session = CreateSession();
        using var caller = new CancellationTokenSource();
        _github.OnTokenRequest = caller.Cancel;

        GitHubTokenResult result = await session.GetTokenAsync(caller.Token);

        Assert.Equal("ghu_new", result.Token!.Value);
        Assert.Equal("ghr_rotated", _store.Stored!.RefreshToken);
    }

    [Fact]
    public async Task a_refreshed_pair_is_kept_in_memory_when_persisting_it_fails()
    {
        _store.Stored = Credentials("ghu_old", Start.AddMinutes(1));
        _github.EnqueueToken(FakeGitHubOAuthHandler.Tokens("ghu_new", refresh: "ghr_rotated"));
        GitHubUserSession session = CreateSession();
        _store.SaveFailure = new IOException("Disk full.");

        GitHubTokenResult first = await session.GetTokenAsync(Ct);
        GitHubTokenResult second = await session.GetTokenAsync(Ct);

        Assert.Equal("ghu_new", first.Token!.Value);
        Assert.Equal("ghu_new", second.Token!.Value);
        Assert.Single(_github.TokenRequests);
        Assert.True(session.Status.IsSignedIn);
    }

    [Fact]
    public async Task a_rejected_refresh_token_ends_the_sign_in_and_signals_the_change()
    {
        _store.Stored = Credentials("ghu_old", Start.AddMinutes(1));
        _github.EnqueueToken(FakeGitHubOAuthHandler.Error("bad_refresh_token"));
        GitHubUserSession session = CreateSession();
        int changes = 0;
        session.Changed += () => changes++;

        GitHubTokenResult result = await session.GetTokenAsync(Ct);

        Assert.False(result.IsAvailable);
        Assert.Contains("sign in with GitHub", result.UnavailableReason);
        Assert.False(session.Status.IsSignedIn);
        Assert.Null(_store.Stored);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task a_failed_refresh_keeps_using_a_token_that_has_not_expired_yet()
    {
        _store.Stored = Credentials("ghu_still_valid", Start.AddMinutes(3));
        _github.TokenEndpointUnreachable = true;

        GitHubTokenResult result = await CreateSession().GetTokenAsync(Ct);

        Assert.Equal("ghu_still_valid", result.Token!.Value);
        Assert.NotNull(_store.Stored);
    }

    [Fact]
    public async Task a_failed_refresh_of_an_expired_token_is_unavailable_but_keeps_the_sign_in_for_the_next_attempt()
    {
        _store.Stored = Credentials("ghu_expired", Start.AddMinutes(-1));
        _github.TokenEndpointUnreachable = true;
        GitHubUserSession session = CreateSession();

        GitHubTokenResult result = await session.GetTokenAsync(Ct);

        Assert.False(result.IsAvailable);
        Assert.Contains("Connection refused", result.UnavailableReason);
        Assert.True(session.Status.IsSignedIn);
    }

    [Fact]
    public async Task an_expired_refresh_token_ends_the_sign_in_once_the_access_token_expired()
    {
        _store.Stored = Credentials("ghu_expired", Start.AddMinutes(-1)) with { RefreshTokenExpiresAt = Start.AddMinutes(-1) };
        GitHubUserSession session = CreateSession();

        GitHubTokenResult result = await session.GetTokenAsync(Ct);

        Assert.False(result.IsAvailable);
        Assert.False(session.Status.IsSignedIn);
        Assert.Empty(_github.Requests);
    }

    [Fact]
    public async Task non_expiring_user_tokens_are_never_refreshed()
    {
        _store.Stored = Credentials("ghu_forever", accessExpiresAt: null) with { RefreshToken = null, RefreshTokenExpiresAt = null };
        GitHubUserSession session = CreateSession();
        _clock.Advance(TimeSpan.FromDays(400));

        GitHubTokenResult result = await session.GetTokenAsync(Ct);

        Assert.Equal("ghu_forever", result.Token!.Value);
        Assert.Null(result.Token.ExpiresAt);
        Assert.Null(session.Status.SignInExpiresAt);
        Assert.Empty(_github.Requests);
    }

    [Fact]
    public async Task signing_out_forgets_and_revokes_the_token_and_signals_the_change()
    {
        _store.Stored = Credentials("ghu_current", Start.AddHours(1));
        GitHubUserSession session = CreateSession();
        int changes = 0;
        session.Changed += () => changes++;

        await session.SignOutAsync(Ct);

        Assert.False(session.Status.IsSignedIn);
        Assert.Null(_store.Stored);
        Assert.False((await session.GetTokenAsync(Ct)).IsAvailable);
        OAuthRequest revoke = Assert.Single(_github.Requests, request => request.Method == HttpMethod.Delete);
        Assert.Equal("/applications/Iv23.client/token", revoke.Uri.AbsolutePath);
        Assert.StartsWith("Basic ", revoke.Authorization);
        Assert.Contains("ghu_current", revoke.Body);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void status_reports_whether_sign_in_is_configured()
    {
        Assert.True(CreateSession().Status.IsConfigured);
        Assert.False(new GitHubUserSession(new HttpClient(_github), _clock, new GitHubAuthOptions { AppClientId = "Iv23.client" }, _store).Status.IsConfigured);
    }

    private GitHubUserSession CreateSession() => new(
        new HttpClient(_github),
        _clock,
        new GitHubAuthOptions { AppClientId = "Iv23.client", AppClientSecret = "client-secret" },
        _store);

    private static GitHubUserCredentials Credentials(string accessToken, DateTimeOffset? accessExpiresAt) => new()
    {
        AccessToken = accessToken,
        AccessTokenExpiresAt = accessExpiresAt,
        RefreshToken = "ghr_stored",
        RefreshTokenExpiresAt = Start.AddDays(180),
        Login = "octocat",
        UserId = 1,
    };
}
