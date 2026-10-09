using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Integration;

internal static class IssueReferences
{
    /// <summary>GitHub's short form <c>#n</c> within the repository, <c>owner/repo#n</c> across repositories.</summary>
    public static string Format(IssueRef issue, GitHubRepoRef repository) =>
        string.Equals(issue.Owner, repository.Owner, StringComparison.OrdinalIgnoreCase)
        && string.Equals(issue.Repo, repository.Name, StringComparison.OrdinalIgnoreCase)
            ? $"#{issue.Number}"
            : issue.ToString();
}
