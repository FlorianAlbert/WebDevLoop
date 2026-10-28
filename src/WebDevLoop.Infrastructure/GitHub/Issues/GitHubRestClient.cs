using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.GitHub.Issues;

/// <summary>Minimal authenticated GitHub REST transport: fresh token per call, pagination, and error mapping.</summary>
internal sealed partial class GitHubRestClient(HttpClient http, ITokenProvider tokens)
{
    private const string ApiVersion = "2026-03-10";
    private const int PageSize = 100;
    private static readonly Uri DefaultBaseAddress = new("https://api.github.com/");

    private readonly Uri _baseAddress = http.BaseAddress ?? DefaultBaseAddress;

    public async Task<JsonElement> GetAsync(GitHubRepoRef repo, string path, CancellationToken cancellationToken)
    {
        JsonElement? result = await SendAsync(HttpMethod.Get, repo, new Uri(_baseAddress, path), null, cancellationToken);
        return result ?? throw new GitHubApiException(GitHubApiErrorKind.Invalid, $"GitHub returned no content for GET {path}.");
    }

    public async Task<IReadOnlyList<JsonElement>> GetAllAsync(GitHubRepoRef repo, string path, CancellationToken cancellationToken)
    {
        var items = new List<JsonElement>();
        Uri? next = new(_baseAddress, $"{path}{(path.Contains('?') ? '&' : '?')}per_page={PageSize}");

        while (next is not null)
        {
            (JsonElement? page, next) = await SendForPageAsync(HttpMethod.Get, repo, next, null, cancellationToken);
            if (page is { ValueKind: JsonValueKind.Array } array)
            {
                items.AddRange(array.EnumerateArray());
            }
            else
            {
                throw new GitHubApiException(GitHubApiErrorKind.Invalid, $"GitHub returned no valid list for GET {path}.");
            }
        }

        return items;
    }

    public async Task<(IReadOnlyList<JsonElement> Items, bool HasMore)> GetPageAsync(
        GitHubRepoRef repo, string path, CancellationToken cancellationToken)
    {
        (JsonElement? content, Uri? next) = await SendForPageAsync(HttpMethod.Get, repo, new Uri(_baseAddress, path), null, cancellationToken);
        return content is { ValueKind: JsonValueKind.Array } array
            ? (array.EnumerateArray().ToArray(), next is not null)
            : throw new GitHubApiException(GitHubApiErrorKind.Invalid, $"GitHub returned no issue list for GET {path}.");
    }

    public async Task<JsonElement?> SendAsync(HttpMethod method, GitHubRepoRef repo, string path, object? body, CancellationToken cancellationToken) =>
        await SendAsync(method, repo, new Uri(_baseAddress, path), body, cancellationToken);

    private async Task<JsonElement?> SendAsync(HttpMethod method, GitHubRepoRef repo, Uri uri, object? body, CancellationToken cancellationToken) =>
        (await SendForPageAsync(method, repo, uri, body, cancellationToken)).Content;

    private async Task<(JsonElement? Content, Uri? Next)> SendForPageAsync(
        HttpMethod method, GitHubRepoRef repo, Uri uri, object? body, CancellationToken cancellationToken)
    {
        GitHubTokenResult token = await tokens.GetTokenAsync(cancellationToken);
        if (!token.IsAvailable)
        {
            throw new GitHubApiException(GitHubApiErrorKind.Unauthorized, $"No GitHub token available for {repo}: {token.UnavailableReason}");
        }

        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new("Bearer", token.Token.Value);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.UserAgent.ParseAdd("WebDevLoop");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using HttpResponseMessage response = await SendMappingNetworkFailuresAsync(request, cancellationToken);
        string content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw MapError(response, content, $"{method} {uri.AbsolutePath}");
        }

        try
        {
            using JsonDocument? document = string.IsNullOrWhiteSpace(content) ? null : JsonDocument.Parse(content);
            return (document?.RootElement.Clone(), FindNextPage(response));
        }
        catch (JsonException exception)
        {
            throw new GitHubApiException(GitHubApiErrorKind.Invalid, $"GitHub returned invalid JSON for {method} {uri.AbsolutePath}. Retry the lookup.", innerException: exception);
        }
    }

    private async Task<HttpResponseMessage> SendMappingNetworkFailuresAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await http.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new GitHubApiException(GitHubApiErrorKind.Transient, $"GitHub request failed: {exception.Message}", innerException: exception);
        }
    }

    private Uri? FindNextPage(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out IEnumerable<string>? links))
        {
            return null;
        }

        Match match = NextLink().Match(string.Join(',', links));
        if (!match.Success)
        {
            return null;
        }

        // Never forward the token to a host other than the configured API.
        return Uri.TryCreate(match.Groups[1].Value, UriKind.Absolute, out Uri? next)
            && next.Authority == _baseAddress.Authority && next.Scheme == _baseAddress.Scheme
            ? next
            : throw new GitHubApiException(GitHubApiErrorKind.Invalid, "GitHub returned an unsafe or invalid next-page URL; the issue list could not be verified.");
    }

    private static GitHubApiException MapError(HttpResponseMessage response, string content, string operation)
    {
        HttpStatusCode status = response.StatusCode;
        string message = $"GitHub {operation} failed with {(int)status} {status}: {ExtractMessage(content)}";
        GitHubApiErrorKind kind = status switch
        {
            HttpStatusCode.Unauthorized => GitHubApiErrorKind.Unauthorized,
            HttpStatusCode.Forbidden when IsRateLimited(response, content) => GitHubApiErrorKind.Transient,
            HttpStatusCode.Forbidden => GitHubApiErrorKind.Forbidden,
            HttpStatusCode.NotFound => GitHubApiErrorKind.NotFound,
            HttpStatusCode.Conflict => GitHubApiErrorKind.Conflict,
            HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests => GitHubApiErrorKind.Transient,
            >= HttpStatusCode.InternalServerError => GitHubApiErrorKind.Transient,
            _ => GitHubApiErrorKind.Invalid,
        };

        return new GitHubApiException(kind, message, status);
    }

    private static bool IsRateLimited(HttpResponseMessage response, string content) =>
        response.Headers.RetryAfter is not null
        || (response.Headers.TryGetValues("X-RateLimit-Remaining", out IEnumerable<string>? remaining) && remaining.Contains("0"))
        || content.Contains("rate limit", StringComparison.OrdinalIgnoreCase);

    private static string ExtractMessage(string content)
    {
        try
        {
            return JsonDocument.Parse(content).RootElement.GetProperty("message").GetString() ?? content;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return content;
        }
    }

    [GeneratedRegex("<([^>]+)>;\\s*rel=\"next\"")]
    private static partial Regex NextLink();
}
