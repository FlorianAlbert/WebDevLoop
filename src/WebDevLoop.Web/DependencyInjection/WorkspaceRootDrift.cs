using WebDevLoop.Core.Domain;

namespace WebDevLoop.Web.DependencyInjection;

/// <summary>Finds registered repositories whose clone lies outside the workspace root the process runs with (the root was changed after they were cloned).</summary>
public static class WorkspaceRootDrift
{
    public static IReadOnlyList<RepositoryRecord> FindOutside(string workspaceRoot, IEnumerable<RepositoryRecord> repositories)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        return [.. repositories.Where(repository => !IsInside(root, repository.LocalPath))];
    }

    private static bool IsInside(string root, string path)
    {
        string relative = Path.GetRelativePath(root, Path.GetFullPath(path));
        return relative != "."
            && !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
