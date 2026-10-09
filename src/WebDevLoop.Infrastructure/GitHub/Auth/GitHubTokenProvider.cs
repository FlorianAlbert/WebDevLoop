using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.GitHub.Auth;

/// <summary>
/// Mints repo- and permission-scoped GitHub App installation tokens in process, caches them until they are close to
/// expiry, and hands out the configured PAT/user token only when fallback is enabled and the caller asked for it.
/// </summary>
public sealed class GitHubTokenProvider : ITokenProvider
{
    private const string UserTokenIdentity = "user-token";
    private const string ApiVersion = "2026-03-10";

    private readonly HttpClient _http;
    private readonly IClock _clock;
    private readonly GitHubAuthOptions _options;
    private readonly GitHubAppJwtFactory _jwtFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<GitHubRepoRef, long> _installationIds = [];
    private readonly Dictionary<(long InstallationId, GitHubRepoRef Repo, string Permissions), GitHubAccessToken> _cache = [];
    private readonly Dictionary<long, int> _generations = [];

    public GitHubTokenProvider(HttpClient http, IClock clock, GitHubAuthOptions options)
    {
        _http = http;
        _clock = clock;
        _options = options;
        _jwtFactory = new GitHubAppJwtFactory(options, clock);
    }

    public async Task<GitHubTokenResult> GetTokenAsync(GitHubTokenRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            AppTokenOutcome app = await GetAppTokenAsync(request, cancellationToken);
            return app.Token is not null
                ? GitHubTokenResult.Available(app.Token)
                : ResolveFallback(request, app.FailureReason!);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AppTokenOutcome> GetAppTokenAsync(GitHubTokenRequest request, CancellationToken cancellationToken)
    {
        if (!_options.IsAppConfigured)
        {
            return AppTokenOutcome.Failed("The GitHub App is not configured.");
        }

        try
        {
            long? installationId = await GetInstallationIdAsync(request.Repo, cancellationToken);
            if (installationId is null)
            {
                return AppTokenOutcome.Failed($"The GitHub App is not installed on {request.Repo} or cannot access it.");
            }

            var cacheKey = (installationId.Value, request.Repo, request.Permissions.ToString());
            if (_cache.TryGetValue(cacheKey, out GitHubAccessToken? cached) && !IsExpiring(cached))
            {
                return AppTokenOutcome.Succeeded(cached);
            }

            return await MintAsync(installationId.Value, request, cacheKey, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            return AppTokenOutcome.Failed($"GitHub request failed: {exception.Message}");
        }
    }

    private bool IsExpiring(GitHubAccessToken token) => token.ExpiresAt is { } expiresAt && expiresAt - _options.ExpirySkew <= _clock.UtcNow;

    private async Task<long?> GetInstallationIdAsync(GitHubRepoRef repo, CancellationToken cancellationToken)
    {
        if (_installationIds.TryGetValue(repo, out long known))
        {
            return known;
        }

        using HttpRequestMessage lookup = CreateAppRequest(HttpMethod.Get, $"repos/{repo.Owner}/{repo.Name}/installation");
        using HttpResponseMessage response = await _http.SendAsync(lookup, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var installation = await response.Content.ReadFromJsonAsync<InstallationResponse>(cancellationToken);
        _installationIds[repo] = installation!.Id;
        return installation.Id;
    }

    private async Task<AppTokenOutcome> MintAsync(
        long installationId,
        GitHubTokenRequest request,
        (long InstallationId, GitHubRepoRef Repo, string Permissions) cacheKey,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage mint = CreateAppRequest(HttpMethod.Post, $"app/installations/{installationId}/access_tokens");
        mint.Content = JsonContent.Create(new MintRequest(
            [request.Repo.Name],
            request.Permissions.Permissions.ToDictionary(entry => entry.Key, entry => entry.Value.ToString().ToLowerInvariant())));

        using HttpResponseMessage response = await _http.SendAsync(mint, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return AppTokenOutcome.Failed(
                $"GitHub rejected the installation token request for {request.Repo} (HTTP {(int)response.StatusCode}).");
        }

        var minted = await response.Content.ReadFromJsonAsync<MintResponse>(cancellationToken);
        int generation = _generations.GetValueOrDefault(installationId) + 1;
        _generations[installationId] = generation;
        var token = new GitHubAccessToken(
            minted!.Token,
            GitHubTokenKind.AppInstallation,
            installationId.ToString(),
            generation,
            minted.ExpiresAt);
        _cache[cacheKey] = token;
        return AppTokenOutcome.Succeeded(token);
    }

    private HttpRequestMessage CreateAppRequest(HttpMethod method, string relativePath)
    {
        var request = new HttpRequestMessage(method, new Uri(new Uri(_options.ApiBaseUrl), relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jwtFactory.Create());
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.UserAgent.ParseAdd("WebDevLoop");
        return request;
    }

    private GitHubTokenResult ResolveFallback(GitHubTokenRequest request, string appFailure)
    {
        if (!request.AllowUserTokenFallback)
        {
            return GitHubTokenResult.Unavailable(appFailure);
        }

        if (!_options.PatFallbackEnabled)
        {
            return GitHubTokenResult.Unavailable($"{appFailure} PAT fallback is disabled.");
        }

        if (string.IsNullOrWhiteSpace(_options.UserToken))
        {
            return GitHubTokenResult.Unavailable($"{appFailure} PAT fallback is enabled but no user token is configured.");
        }

        return GitHubTokenResult.Available(new GitHubAccessToken(_options.UserToken, GitHubTokenKind.UserToken, UserTokenIdentity, 1, null));
    }

    private sealed record AppTokenOutcome(GitHubAccessToken? Token, string? FailureReason)
    {
        public static AppTokenOutcome Succeeded(GitHubAccessToken token) => new(token, null);

        public static AppTokenOutcome Failed(string reason) => new(null, reason);
    }

    private sealed record InstallationResponse([property: JsonPropertyName("id")] long Id);

    private sealed record MintRequest(
        [property: JsonPropertyName("repositories")] string[] Repositories,
        [property: JsonPropertyName("permissions")] Dictionary<string, string> Permissions);

    private sealed record MintResponse(
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);
}
