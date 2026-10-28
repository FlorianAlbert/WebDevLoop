using System.Net;
using System.Text.Json;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.GitHub.Issues;

/// <summary>Repository-only label provisioning. A successful, paginated list confirms absence; a 404 does not.</summary>
public sealed class GitHubRepositoryLabels(HttpClient http, ITokenProvider tokens) : IGitHubRepositoryLabels
{
    private readonly GitHubRestClient _api = new(http, tokens);

    public async Task<bool> HasOptInLabelAsync(GitHubRepoRef repository, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<JsonElement> labels = await _api.GetAllAsync(repository, PathFor(repository), cancellationToken);
            return labels.Any(label => string.Equals(label.GetProperty("name").GetString(), WebDevLoopOptIn.Label, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is GitHubApiException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw Failure(exception);
        }
    }

    public async Task CreateOptInLabelAsync(GitHubRepoRef repository, CancellationToken cancellationToken)
    {
        try
        {
            JsonElement? created = await _api.SendAsync(HttpMethod.Post, repository, PathFor(repository),
                new { name = WebDevLoopOptIn.Label, color = "5319e7", description = "Explicit permission for WebDevLoop to work on this issue." }, cancellationToken);
            if (created is null || !string.Equals(created.Value.GetProperty("name").GetString(), WebDevLoopOptIn.Label, StringComparison.OrdinalIgnoreCase))
            {
                throw new GitHubApiException(GitHubApiErrorKind.Invalid, "GitHub did not confirm the created label.");
            }
        }
        catch (GitHubApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Another approved request may have created the same label while this request was in flight.
            if (!await HasOptInLabelAsync(repository, cancellationToken))
            {
                throw Failure(exception);
            }
        }
        catch (Exception exception) when (exception is GitHubApiException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw Failure(exception);
        }
    }

    private static string PathFor(GitHubRepoRef repository) => $"repos/{repository.Owner}/{repository.Name}/labels";

    private static RepositoryLabelException Failure(Exception exception) => new(
        $"Check GitHub sign-in, repository access and Issues read/write permissions, then retry. {exception.Message}", exception);
}
