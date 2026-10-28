using System.Text.Json;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.GitHub.Issues;

internal static class IssueJson
{
    private const string RepositoriesSegment = "/repos/";

    public static IssueRef ToRef(JsonElement issue, GitHubRepoRef fallbackRepo)
    {
        try
        {
            GitHubRepoRef repo = issue.TryGetProperty("repository_url", out JsonElement url) && TryParseRepo(url.GetString(), out GitHubRepoRef parsed)
                ? parsed
                : fallbackRepo;
            string? nodeId = issue.TryGetProperty("node_id", out JsonElement node) ? node.GetString() : null;
            long? databaseId = issue.TryGetProperty("id", out JsonElement id) ? id.GetInt64() : null;

            return new IssueRef(repo.Owner, repo.Name, issue.GetProperty("number").GetInt32(), nodeId, databaseId);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new GitHubApiException(GitHubApiErrorKind.Invalid, "GitHub returned an invalid issue reference.", innerException: exception);
        }
    }

    public static IssueSnapshot ToSnapshot(JsonElement issue, GitHubRepoRef fallbackRepo, IReadOnlyList<IssueRef> blockedBy)
    {
        try
        {
            return new IssueSnapshot(
                ToRef(issue, fallbackRepo),
                issue.GetProperty("title").GetString() ?? string.Empty,
                Body(issue) ?? string.Empty,
                State(issue),
                blockedBy)
            {
                Labels = Labels(issue),
            };
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            throw new GitHubApiException(GitHubApiErrorKind.Invalid, "GitHub returned incomplete issue data.", innerException: exception);
        }
    }

    private static IssueState State(JsonElement issue)
    {
        if (issue.TryGetProperty("state", out JsonElement state) && state.ValueKind == JsonValueKind.String)
        {
            return state.GetString() switch
            {
                "open" => IssueState.Open,
                "closed" => IssueState.Closed,
                _ => throw new GitHubApiException(GitHubApiErrorKind.Invalid, "GitHub returned an unknown issue state."),
            };
        }

        throw new GitHubApiException(GitHubApiErrorKind.Invalid, "GitHub did not return the required issue state.");
    }

    private static IReadOnlyList<string> Labels(JsonElement issue)
    {
        if (!issue.TryGetProperty("labels", out JsonElement labels) || labels.ValueKind != JsonValueKind.Array)
        {
            throw new GitHubApiException(GitHubApiErrorKind.Invalid, "GitHub did not return the required issue labels.");
        }

        var names = new List<string>();
        foreach (JsonElement label in labels.EnumerateArray())
        {
            if (label.ValueKind != JsonValueKind.Object
                || !label.TryGetProperty("name", out JsonElement name)
                || name.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(name.GetString()))
            {
                throw new GitHubApiException(GitHubApiErrorKind.Invalid, "GitHub returned invalid issue labels.");
            }

            names.Add(name.GetString()!);
        }

        return names;
    }

    public static string? Body(JsonElement issue) =>
        issue.TryGetProperty("body", out JsonElement body) && body.ValueKind == JsonValueKind.String ? body.GetString() : null;

    private static bool TryParseRepo(string? repositoryUrl, out GitHubRepoRef repo)
    {
        repo = default;
        int index = repositoryUrl?.LastIndexOf(RepositoriesSegment, StringComparison.Ordinal) ?? -1;
        if (index < 0)
        {
            return false;
        }

        string[] parts = repositoryUrl![(index + RepositoriesSegment.Length)..].Split('/');
        if (parts.Length != 2)
        {
            return false;
        }

        repo = new GitHubRepoRef(parts[0], parts[1]);
        return true;
    }
}
