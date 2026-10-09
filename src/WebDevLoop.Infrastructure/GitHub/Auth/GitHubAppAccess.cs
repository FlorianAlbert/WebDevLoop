using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Pulls;

namespace WebDevLoop.Infrastructure.GitHub.Auth;

/// <param name="Account">The user or organization the App is installed on.</param>
/// <param name="AllRepositories">The installation covers all of the account's repositories (now and in future).</param>
/// <param name="ConfigureUrl">The GitHub page where the user adds or removes repositories of the installation.</param>
/// <param name="Repositories">The repositories the signed-in user reaches through this installation (<c>owner/name</c>).</param>
public sealed record GitHubAppInstallation(
    long Id,
    string Account,
    bool AllRepositories,
    string ConfigureUrl,
    string? AppSlug,
    IReadOnlyList<string> Repositories);

/// <summary>
/// Which repositories the signed-in user's token reaches: those of the App's installations the user can access. Adding or
/// removing repositories happens on GitHub (the installation's configure page), never here.
/// </summary>
public sealed partial class GitHubAppAccess(HttpClient http, ITokenProvider tokens, GitHubAuthOptions options)
{
    private const string ApiVersion = "2026-03-10";
    private const int PageSize = 100;

    /// <exception cref="GitHubTokenUnavailableException">Nobody is signed in.</exception>
    /// <exception cref="HttpRequestException">GitHub could not be reached or rejected the request.</exception>
    public async Task<IReadOnlyList<GitHubAppInstallation>> ListInstallationsAsync(CancellationToken cancellationToken)
    {
        GitHubTokenResult token = await tokens.GetTokenAsync(cancellationToken);
        if (!token.IsAvailable)
        {
            throw new GitHubTokenUnavailableException(token.UnavailableReason);
        }

        var installations = new List<GitHubAppInstallation>();
        foreach (JsonElement installation in await GetAllAsync(token.Token.Value, "user/installations", "installations", cancellationToken))
        {
            long id = installation.GetProperty("id").GetInt64();
            IReadOnlyList<JsonElement> repositories = await GetAllAsync(
                token.Token.Value, $"user/installations/{id}/repositories", "repositories", cancellationToken);
            installations.Add(new GitHubAppInstallation(
                id,
                installation.GetProperty("account").GetProperty("login").GetString()!,
                installation.TryGetProperty("repository_selection", out JsonElement selection) && selection.GetString() == "all",
                installation.GetProperty("html_url").GetString()!,
                installation.TryGetProperty("app_slug", out JsonElement slug) ? slug.GetString() : null,
                repositories.Select(repository => repository.GetProperty("full_name").GetString()!).Order(StringComparer.OrdinalIgnoreCase).ToArray()));
        }

        return installations;
    }

    /// <summary>Where to install the App on another account; null when the App's slug is neither configured nor known from an installation.</summary>
    public Uri? InstallUri(IReadOnlyList<GitHubAppInstallation> installations)
    {
        string? slug = string.IsNullOrWhiteSpace(options.AppSlug)
            ? installations.Select(installation => installation.AppSlug).FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate))
            : options.AppSlug;
        return slug is null ? null : new Uri(new Uri(options.WebBaseUrl), $"apps/{Uri.EscapeDataString(slug)}/installations/new");
    }

    private async Task<IReadOnlyList<JsonElement>> GetAllAsync(string token, string path, string itemsProperty, CancellationToken cancellationToken)
    {
        var items = new List<JsonElement>();
        Uri? next = new(new Uri(options.ApiBaseUrl), $"{path}?per_page={PageSize}");
        while (next is not null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, next);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
            request.Headers.UserAgent.ParseAdd("WebDevLoop");
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using JsonDocument page = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (page.RootElement.TryGetProperty(itemsProperty, out JsonElement array) && array.ValueKind == JsonValueKind.Array)
            {
                items.AddRange(array.EnumerateArray().Select(item => item.Clone()));
            }

            next = NextPage(response);
        }

        return items;
    }

    private static Uri? NextPage(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out IEnumerable<string>? links))
        {
            return null;
        }

        Match match = NextLink().Match(string.Join(',', links));
        return match.Success ? new Uri(match.Groups["url"].Value) : null;
    }

    [GeneratedRegex("<(?<url>[^>]+)>;\\s*rel=\"next\"")]
    private static partial Regex NextLink();
}
