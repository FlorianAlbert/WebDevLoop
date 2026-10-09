using System.Net;
using System.Text;
using System.Text.Json;
using WebDevLoop.Infrastructure.GitHub.Auth;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Auth;

internal sealed record OAuthRequest(HttpMethod Method, Uri Uri, string? Authorization, string Body)
{
    public IReadOnlyDictionary<string, string> Form =>
        Body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')));
}

/// <summary>Stands in for github.com's token endpoint and api.github.com's user, token-revocation and installation endpoints.</summary>
internal sealed class FakeGitHubOAuthHandler : HttpMessageHandler
{
    private readonly Queue<object> _tokenResponses = new();
    private readonly Dictionary<string, (string Body, string? NextLink)> _api = [];

    public List<OAuthRequest> Requests { get; } = [];

    public object User { get; set; } = new { login = "octocat", id = 1, avatar_url = "https://avatars.example/octocat" };

    public bool TokenEndpointUnreachable { get; set; }

    /// <summary>Runs when the token endpoint is hit, before the response; the request's cancellation token is honoured afterwards like a real handler.</summary>
    public Action? OnTokenRequest { get; set; }

    public IEnumerable<OAuthRequest> TokenRequests => Requests.Where(request => request.Uri.AbsolutePath == "/login/oauth/access_token");

    public static object Tokens(string access, long? expiresIn = 28800, string? refresh = "ghr_refresh", long? refreshExpiresIn = 15897600) =>
        new { access_token = access, expires_in = expiresIn, refresh_token = refresh, refresh_token_expires_in = refreshExpiresIn, token_type = "bearer" };

    public static object Error(string error, string description = "rejected") => new { error, error_description = description };

    public void EnqueueToken(object response) => _tokenResponses.Enqueue(response);

    public void Api(string pathAndQuery, object body, string? nextLink = null) => _api[pathAndQuery] = (JsonSerializer.Serialize(body), nextLink);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new OAuthRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));

        Uri uri = request.RequestUri!;
        if (uri.AbsolutePath == "/login/oauth/access_token")
        {
            if (TokenEndpointUnreachable)
            {
                throw new HttpRequestException("Connection refused.");
            }

            OnTokenRequest?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();

            return Json(HttpStatusCode.OK, _tokenResponses.Count > 0 ? _tokenResponses.Dequeue() : Error("unexpected"));
        }

        if (uri.AbsolutePath == "/user")
        {
            return Json(HttpStatusCode.OK, User);
        }

        if (request.Method == HttpMethod.Delete && uri.AbsolutePath.StartsWith("/applications/", StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        if (_api.TryGetValue(uri.PathAndQuery, out (string Body, string? NextLink) api))
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(api.Body, Encoding.UTF8, "application/json") };
            if (api.NextLink is not null)
            {
                response.Headers.Add("Link", $"<{api.NextLink}>; rel=\"next\"");
            }

            return response;
        }

        return Json(HttpStatusCode.NotFound, new { message = "unexpected request" });
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object content) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(content), Encoding.UTF8, "application/json") };
}

internal sealed class InMemoryCredentialStore : IGitHubCredentialStore
{
    public GitHubUserCredentials? Stored { get; set; }

    public int Saves { get; private set; }

    public Exception? SaveFailure { get; set; }

    public GitHubUserCredentials? Load() => Stored;

    public void Save(GitHubUserCredentials credentials)
    {
        Saves++;
        if (SaveFailure is not null)
        {
            throw SaveFailure;
        }

        Stored = credentials;
    }

    public void Clear() => Stored = null;
}
