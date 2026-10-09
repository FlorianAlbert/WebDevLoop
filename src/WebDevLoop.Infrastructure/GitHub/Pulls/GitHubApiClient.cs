using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.GitHub.Pulls;

internal sealed record GitHubApiResponse(HttpStatusCode Status, string Content)
{
    public bool IsSuccess => (int)Status is >= 200 and < 300;

    public T Read<T>() => JsonSerializer.Deserialize<T>(Content, GitHubApiClient.Json)
        ?? throw new GitHubApiException(Status, "GitHub returned an empty response.");

    public GitHubApiResponse EnsureSuccess() => IsSuccess ? this : throw ToException();

    public GitHubApiException ToException() => new(Status, $"GitHub request failed (HTTP {(int)Status}): {ErrorMessage()}");

    private string ErrorMessage()
    {
        try
        {
            return JsonNode.Parse(Content)?["message"]?.GetValue<string>() ?? "no message";
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return "no message";
        }
    }
}

/// <summary>Sends REST/GraphQL calls with a freshly requested, permission-scoped token; the token is never stored or logged.</summary>
internal sealed class GitHubApiClient(HttpClient http, ITokenProvider tokens, GitHubPullsOptions options)
{
    private const string ApiVersion = "2026-03-10";

    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task<GitHubAccessToken> AcquireTokenAsync(GitHubRepoRef repo, CancellationToken cancellationToken)
    {
        GitHubTokenResult result = await tokens.GetTokenAsync(
            new GitHubTokenRequest(repo, GitHubPermissionSet.PullRequestsWrite, options.AllowUserTokenFallback),
            cancellationToken);

        return result.IsAvailable ? result.Token : throw new GitHubTokenUnavailableException(result.UnavailableReason);
    }

    public Task<GitHubApiResponse> SendAsync(
        HttpMethod method,
        GitHubRepoRef repo,
        string path,
        object? body,
        CancellationToken cancellationToken) =>
        SendAsync(method, new Uri(new Uri(options.ApiBaseUrl), $"repos/{repo.Owner}/{repo.Name}/{path}"), repo, body, cancellationToken);

    /// <summary>Runs a GraphQL operation; HTTP 200 with an <c>errors</c> array is treated as failure.</summary>
    public async Task<JsonNode> GraphQlAsync(GitHubRepoRef repo, string query, object variables, CancellationToken cancellationToken)
    {
        GitHubApiResponse response = (await SendAsync(HttpMethod.Post, new Uri(options.GraphQlUrl), repo, new { query, variables }, cancellationToken))
            .EnsureSuccess();
        JsonNode root = JsonNode.Parse(response.Content) ?? throw new GitHubApiException(response.Status, "GitHub returned an empty GraphQL response.");

        if (root["errors"] is JsonArray { Count: > 0 } errors)
        {
            string messages = string.Join("; ", errors.Select(error => error?["message"]?.GetValue<string>() ?? "unknown error"));
            throw new GitHubApiException(response.Status, $"GitHub GraphQL request failed: {messages}");
        }

        return root["data"] ?? throw new GitHubApiException(response.Status, "GitHub GraphQL response has no data.");
    }

    private async Task<GitHubApiResponse> SendAsync(
        HttpMethod method,
        Uri uri,
        GitHubRepoRef repo,
        object? body,
        CancellationToken cancellationToken)
    {
        GitHubAccessToken token = await AcquireTokenAsync(repo, cancellationToken);
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.UserAgent.ParseAdd("WebDevLoop");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        return new GitHubApiResponse(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
