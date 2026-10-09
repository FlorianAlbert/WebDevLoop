using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.GitHub.Auth;

/// <summary>
/// Signs the user in with the GitHub App's web application flow (authorization code with PKCE), keeps the user access
/// token and its refresh token in the <see cref="IGitHubCredentialStore"/>, and hands the token out as the app's GitHub
/// credential (<see cref="ITokenProvider"/>), refreshing it before it expires. A refresh token GitHub rejects (or that
/// expired) ends the sign-in. <see cref="Changed"/> fires when the user signs in, signs out, or the sign-in ends.
/// </summary>
/// <remarks>
/// GitHub refresh tokens are single-use: once GitHub accepted a refresh, only the returned pair is valid. A refresh is
/// therefore never cancelled by its caller, and the new pair is kept in memory even when persisting it fails.
/// </remarks>
public sealed partial class GitHubUserSession : ITokenProvider, IGitHubSignInState
{
    private const string ApiVersion = "2026-03-10";
    private const string NotSignedInReason = "Nobody is signed in to GitHub; sign in with GitHub in WebDevLoop.";
    private const string SignInEndedReason = "The GitHub sign-in expired or was revoked; sign in with GitHub in WebDevLoop again.";

    private static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly HttpClient _http;
    private readonly IClock _clock;
    private readonly GitHubAuthOptions _options;
    private readonly IGitHubCredentialStore _store;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile GitHubUserCredentials? _credentials;
    private int _generation;

    public GitHubUserSession(HttpClient http, IClock clock, GitHubAuthOptions options, IGitHubCredentialStore store, ILogger<GitHubUserSession>? logger = null)
    {
        _http = http;
        _clock = clock;
        _options = options;
        _store = store;
        _logger = logger ?? NullLogger<GitHubUserSession>.Instance;
        _credentials = store.Load();
        _generation = _credentials is null ? 0 : 1;
    }

    public event Action? Changed;

    public GitHubSignInStatus Status
    {
        get
        {
            GitHubUserCredentials? credentials = _credentials;
            return new GitHubSignInStatus(
                _options.IsConfigured,
                credentials?.Login,
                credentials?.AvatarUrl,
                credentials is null ? null : SignInExpiresAt(credentials));
        }
    }

    /// <summary>Where to send the user to authorize the App; GitHub redirects back to <paramref name="redirectUri"/> with a code.</summary>
    /// <param name="codeChallenge">The base64url SHA-256 of the PKCE code verifier passed to <see cref="CompleteSignInAsync"/>.</param>
    public Uri CreateAuthorizationUri(Uri redirectUri, string state, string codeChallenge)
    {
        RequireConfigured();
        (string Name, string Value)[] query =
        [
            ("client_id", _options.AppClientId!),
            ("redirect_uri", redirectUri.ToString()),
            ("state", state),
            ("code_challenge", codeChallenge),
            ("code_challenge_method", "S256"),
        ];
        string encoded = string.Join('&', query.Select(parameter => $"{parameter.Name}={Uri.EscapeDataString(parameter.Value)}"));
        return new Uri(new Uri(_options.WebBaseUrl), $"login/oauth/authorize?{encoded}");
    }

    /// <summary>Exchanges the authorization code for the user's tokens, looks the user up, and stores the sign-in.</summary>
    /// <exception cref="GitHubSignInException">GitHub rejected the code or could not be reached.</exception>
    public async Task<GitHubSignInStatus> CompleteSignInAsync(string code, Uri redirectUri, string codeVerifier, CancellationToken cancellationToken)
    {
        RequireConfigured();
        DateTimeOffset now = _clock.UtcNow;
        GitHubUserCredentials credentials;
        try
        {
            TokenResponse tokens = await RequestTokensAsync(
                [("code", code), ("redirect_uri", redirectUri.ToString()), ("code_verifier", codeVerifier)],
                cancellationToken);
            if (tokens.Error is not null || string.IsNullOrEmpty(tokens.AccessToken))
            {
                throw new GitHubSignInException($"GitHub rejected the sign-in: {Describe(tokens)}");
            }

            UserResponse user = await GetUserAsync(tokens.AccessToken, cancellationToken);
            credentials = ToCredentials(tokens, now, user.Login, user.Id, user.AvatarUrl);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            throw new GitHubSignInException($"Completing the GitHub sign-in failed: {exception.Message}", exception);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Replace(credentials);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke();
        return Status;
    }

    /// <summary>Forgets the sign-in and revokes the access token on GitHub (best effort).</summary>
    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        GitHubUserCredentials? signedOut;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            signedOut = _credentials;
            Replace(null);
        }
        finally
        {
            _gate.Release();
        }

        if (signedOut is not null)
        {
            await RevokeAsync(signedOut.AccessToken, cancellationToken);
        }

        Changed?.Invoke();
    }

    public async Task<GitHubTokenResult> GetTokenAsync(CancellationToken cancellationToken)
    {
        (GitHubTokenResult Result, bool Ended) outcome;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            outcome = await GetOrRefreshAsync();
        }
        finally
        {
            _gate.Release();
        }

        if (outcome.Ended)
        {
            Changed?.Invoke();
        }

        return outcome.Result;
    }

    /// <summary>Caller holds the gate. GitHub refresh tokens are single-use, so refreshes must never run concurrently.</summary>
    private async Task<(GitHubTokenResult Result, bool Ended)> GetOrRefreshAsync()
    {
        GitHubUserCredentials? credentials = _credentials;
        if (credentials is null)
        {
            return (GitHubTokenResult.Unavailable(NotSignedInReason), false);
        }

        DateTimeOffset now = _clock.UtcNow;
        bool expired = credentials.AccessTokenExpiresAt is { } expiresAt && expiresAt <= now;
        bool expiring = credentials.AccessTokenExpiresAt is { } refreshAt && refreshAt - _options.ExpirySkew <= now;
        if (!expiring)
        {
            return (Available(credentials), false);
        }

        if (credentials.RefreshToken is null || credentials.RefreshTokenExpiresAt <= now)
        {
            return expired ? (EndSignIn(), true) : (Available(credentials), false);
        }

        TokenResponse tokens;
        try
        {
            using var timeout = new CancellationTokenSource(RefreshTimeout);
            tokens = await RequestTokensAsync([("grant_type", "refresh_token"), ("refresh_token", credentials.RefreshToken)], timeout.Token);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException)
        {
            return expired
                ? (GitHubTokenResult.Unavailable($"Refreshing the GitHub token failed: {exception.Message}"), false)
                : (Available(credentials), false);
        }

        if (tokens.Error == "bad_refresh_token")
        {
            return (EndSignIn(), true);
        }

        if (tokens.Error is not null || string.IsNullOrEmpty(tokens.AccessToken))
        {
            return expired
                ? (GitHubTokenResult.Unavailable($"GitHub rejected the token refresh: {Describe(tokens)}"), false)
                : (Available(credentials), false);
        }

        GitHubUserCredentials refreshed = ToCredentials(tokens, now, credentials.Login, credentials.UserId, credentials.AvatarUrl);
        Replace(refreshed);
        return (Available(refreshed), false);
    }

    private GitHubTokenResult EndSignIn()
    {
        Replace(null);
        return GitHubTokenResult.Unavailable(SignInEndedReason);
    }

    /// <summary>
    /// Caller holds the gate. Memory is updated first: a failure to persist only costs the sign-in after a restart, while
    /// losing a refreshed pair would end it right away.
    /// </summary>
    private void Replace(GitHubUserCredentials? credentials)
    {
        if (credentials is not null)
        {
            _generation++;
        }

        _credentials = credentials;
        try
        {
            if (credentials is null)
            {
                _store.Clear();
            }
            else
            {
                _store.Save(credentials);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            LogPersistFailed(_logger, exception);
        }
    }

    private GitHubTokenResult Available(GitHubUserCredentials credentials) =>
        GitHubTokenResult.Available(new GitHubAccessToken(credentials.AccessToken, credentials.Login, _generation, credentials.AccessTokenExpiresAt));

    private static DateTimeOffset? SignInExpiresAt(GitHubUserCredentials credentials) =>
        credentials.RefreshToken is null ? credentials.AccessTokenExpiresAt : credentials.RefreshTokenExpiresAt;

    private static GitHubUserCredentials ToCredentials(TokenResponse tokens, DateTimeOffset now, string login, long userId, string? avatarUrl) => new()
    {
        AccessToken = tokens.AccessToken!,
        AccessTokenExpiresAt = tokens.ExpiresIn is { } expiresIn ? now.AddSeconds(expiresIn) : null,
        RefreshToken = string.IsNullOrEmpty(tokens.RefreshToken) ? null : tokens.RefreshToken,
        RefreshTokenExpiresAt = tokens.RefreshTokenExpiresIn is { } refreshExpiresIn ? now.AddSeconds(refreshExpiresIn) : null,
        Login = login,
        UserId = userId,
        AvatarUrl = avatarUrl,
    };

    private static string Describe(TokenResponse tokens) =>
        tokens.Error is null ? "the response contained no access token." : $"{tokens.ErrorDescription ?? tokens.Error} ({tokens.Error}).";

    /// <summary>GitHub answers token requests with HTTP 200 and an <c>error</c> field when it rejects them.</summary>
    private async Task<TokenResponse> RequestTokensAsync(IEnumerable<(string Name, string Value)> parameters, CancellationToken cancellationToken)
    {
        Dictionary<string, string> form = new()
        {
            ["client_id"] = _options.AppClientId!,
            ["client_secret"] = _options.AppClientSecret!,
        };
        foreach ((string name, string value) in parameters)
        {
            form[name] = value;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(_options.WebBaseUrl), "login/oauth/access_token"))
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("WebDevLoop");
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TokenResponse>(Json, cancellationToken)
            ?? throw new JsonException("GitHub returned an empty token response.");
    }

    private async Task<UserResponse> GetUserAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(_options.ApiBaseUrl), "user"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.UserAgent.ParseAdd("WebDevLoop");
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserResponse>(Json, cancellationToken)
            ?? throw new JsonException("GitHub returned an empty user.");
    }

    private async Task RevokeAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Delete,
                new Uri(new Uri(_options.ApiBaseUrl), $"applications/{Uri.EscapeDataString(_options.AppClientId!)}/token"))
            {
                Content = JsonContent.Create(new { access_token = accessToken }),
            };
            string basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.AppClientId}:{_options.AppClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
            request.Headers.UserAgent.ParseAdd("WebDevLoop");
            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            // Best effort: the token is forgotten either way and expires on its own.
        }
    }

    private void RequireConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException("GitHub sign-in is not configured: set WebDevLoop:GitHub:AppClientId and WebDevLoop:GitHub:AppClientSecret.");
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Persisting the GitHub sign-in failed; it stays valid until WebDevLoop restarts.")]
    private static partial void LogPersistFailed(ILogger logger, Exception exception);

    private sealed record TokenResponse(
        string? AccessToken,
        long? ExpiresIn,
        string? RefreshToken,
        long? RefreshTokenExpiresIn,
        string? Error,
        string? ErrorDescription);

    private sealed record UserResponse(string Login, long Id, string? AvatarUrl);
}
