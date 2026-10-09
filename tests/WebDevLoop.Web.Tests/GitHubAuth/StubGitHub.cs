using System.Net;
using System.Text;
using System.Text.Json;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Auth;

namespace WebDevLoop.Web.Tests.GitHubAuth;

/// <summary>Stands in for github.com's token endpoint and the api.github.com endpoints the sign-in and the GitHub page use.</summary>
internal sealed class StubGitHub : HttpMessageHandler
{
    public const string ClientId = "Iv23.client";
    public const string ClientSecret = "client-secret";

    public List<(Uri Uri, string Body)> Requests { get; } = [];

    public string? IssuedAccessToken { get; set; } = "ghu_signed_in";

    public List<object> Installations { get; } = [];

    public Dictionary<long, string[]> InstallationRepositories { get; } = [];

    public static GitHubAuthOptions Options => new() { AppClientId = ClientId, AppClientSecret = ClientSecret, AppSlug = "webdevloop" };

    public GitHubUserSession CreateSession(IGitHubCredentialStore? store = null) =>
        new(new HttpClient(this), new SystemClockForTests(), Options, store ?? new MemoryCredentialStore());

    public static GitHubUserCredentials SignedIn(string login = "octocat") => new()
    {
        AccessToken = "ghu_stored",
        AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(8),
        RefreshToken = "ghr_stored",
        RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(180),
        Login = login,
        UserId = 1,
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Uri uri = request.RequestUri!;
        Requests.Add((uri, body));
        return uri.AbsolutePath switch
        {
            "/login/oauth/access_token" when IssuedAccessToken is not null => Json(new
            {
                access_token = IssuedAccessToken,
                expires_in = 28800,
                refresh_token = "ghr_issued",
                refresh_token_expires_in = 15897600,
            }),
            "/login/oauth/access_token" => Json(new { error = "bad_verification_code", error_description = "The code passed is incorrect or expired." }),
            "/user" => Json(new { login = "octocat", id = 1, avatar_url = "https://avatars.example/octocat" }),
            "/user/installations" => Json(new { installations = Installations }),
            _ when uri.AbsolutePath.StartsWith("/user/installations/", StringComparison.Ordinal) => Json(new
            {
                repositories = InstallationRepositories[long.Parse(uri.Segments[3].TrimEnd('/'))].Select(name => new { full_name = name }),
            }),
            _ when request.Method == HttpMethod.Delete => new HttpResponseMessage(HttpStatusCode.NoContent),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private static HttpResponseMessage Json(object content) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(content), Encoding.UTF8, "application/json") };

    private sealed class SystemClockForTests : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}

internal sealed class MemoryCredentialStore(GitHubUserCredentials? stored = null) : IGitHubCredentialStore
{
    public GitHubUserCredentials? Stored { get; private set; } = stored;

    public GitHubUserCredentials? Load() => Stored;

    public void Save(GitHubUserCredentials credentials) => Stored = credentials;

    public void Clear() => Stored = null;
}
