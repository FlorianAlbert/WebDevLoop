using System.Text.Json;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.GitHub.Issues;

internal static class IssueJson
{
    private const string RepositoriesSegment = "/repos/";

    public static IssueRef ToRef(JsonElement issue, GitHubRepoRef fallbackRepo)
    {
        GitHubRepoRef repo = issue.TryGetProperty("repository_url", out JsonElement url) && TryParseRepo(url.GetString(), out GitHubRepoRef parsed)
            ? parsed
            : fallbackRepo;
        string? nodeId = issue.TryGetProperty("node_id", out JsonElement node) ? node.GetString() : null;
        long? databaseId = issue.TryGetProperty("id", out JsonElement id) ? id.GetInt64() : null;

        return new IssueRef(repo.Owner, repo.Name, issue.GetProperty("number").GetInt32(), nodeId, databaseId);
    }

    public static IssueSnapshot ToSnapshot(JsonElement issue, GitHubRepoRef fallbackRepo, IReadOnlyList<IssueRef> blockedBy) => new(
        ToRef(issue, fallbackRepo),
        issue.GetProperty("title").GetString() ?? string.Empty,
        Body(issue) ?? string.Empty,
        issue.GetProperty("state").GetString() == "closed" ? IssueState.Closed : IssueState.Open,
        blockedBy);

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
