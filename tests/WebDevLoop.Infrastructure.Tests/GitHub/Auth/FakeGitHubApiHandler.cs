using System.Net;
using System.Text;
using System.Text.Json;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Auth;

internal sealed record RecordedRequest(HttpMethod Method, string Path, string? Authorization, string Body);

/// <summary>Stands in for api.github.com's installation lookup and installation-token endpoints; never touches the network.</summary>
internal sealed class FakeGitHubApiHandler(IClock clock) : HttpMessageHandler
{
    public const long InstallationId = 42;

    private int _mintedTokens;

    public List<RecordedRequest> Requests { get; } = [];

    public HttpStatusCode InstallationLookupStatus { get; set; } = HttpStatusCode.OK;

    public HttpStatusCode MintStatus { get; set; } = HttpStatusCode.Created;

    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(1);

    public int MintedTokens => _mintedTokens;

    public IEnumerable<RecordedRequest> LookupRequests => Requests.Where(request => request.Method == HttpMethod.Get);

    public IEnumerable<RecordedRequest> MintRequests => Requests.Where(request => request.Method == HttpMethod.Post);

    public static string TokenValue(int number) => $"ghs_minted{number}";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString(), body));

        string path = request.RequestUri.AbsolutePath;
        if (request.Method == HttpMethod.Get && path.EndsWith("/installation", StringComparison.Ordinal))
        {
            return Json(InstallationLookupStatus, new { id = InstallationId });
        }

        if (request.Method == HttpMethod.Post && path == $"/app/installations/{InstallationId}/access_tokens")
        {
            if (MintStatus != HttpStatusCode.Created)
            {
                return Json(MintStatus, new { message = "denied" });
            }

            int number = Interlocked.Increment(ref _mintedTokens);
            return Json(MintStatus, new { token = TokenValue(number), expires_at = clock.UtcNow + TokenLifetime });
        }

        return Json(HttpStatusCode.NotFound, new { message = "unexpected request" });
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object content) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(content), Encoding.UTF8, "application/json") };
}
