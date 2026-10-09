using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Preparation;

/// <summary>
/// Where the app clones a registered repository: <c>&lt;root&gt;/repos/&lt;owner&gt;/&lt;name&gt;</c>, a sibling of the app-owned
/// <c>runs</c> directories (see <see cref="RunWorkspaceLayout"/>) and always inside the workspace root the git adapter confines itself to.
/// </summary>
public static class RepositoryCloneLayout
{
    private const string RepositoriesDirectoryName = "repos";

    public static string PathFor(string workspaceRoot, GitHubRepoRef repo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        if (!Path.IsPathFullyQualified(workspaceRoot))
        {
            throw new ArgumentException($"Workspace root '{workspaceRoot}' must be an absolute path.", nameof(workspaceRoot));
        }

        if (!IsSafeSegment(repo.Owner) || !IsSafeSegment(repo.Name))
        {
            throw new ArgumentException($"Repository '{repo}' cannot be mapped to a directory.", nameof(repo));
        }

        return Path.Combine(Path.GetFullPath(workspaceRoot), RepositoriesDirectoryName, repo.Owner, repo.Name);
    }

    /// <summary>GitHub owner and repository names consist of letters, digits, '-', '_' and '.'; "." and ".." would escape the directory.</summary>
    public static bool IsSafeSegment(string segment) =>
        !string.IsNullOrEmpty(segment)
        && segment is not ("." or "..")
        && segment.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}
