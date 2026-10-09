namespace WebDevLoop.Infrastructure.Git;

/// <summary>Confines clone and worktree paths to the configured workspace root.</summary>
/// <remarks>The check is lexical (after normalization); the workspace root is app-owned, so no symlinks are expected inside it.</remarks>
public sealed class WorkspacePathGuard
{
    private const string ParentSegment = "..";
    private readonly string _root;

    public WorkspacePathGuard(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _root = Normalize(workspaceRoot);
    }

    /// <returns>The normalized absolute path.</returns>
    /// <exception cref="WorkspacePathOutsideRootException">The path does not resolve to a location strictly under the root.</exception>
    public string Confine(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string full = Normalize(path);
        string relative = Path.GetRelativePath(_root, full);
        bool outside = relative == "."
            || Path.IsPathRooted(relative)
            || relative == ParentSegment
            || relative.StartsWith(ParentSegment + Path.DirectorySeparatorChar, StringComparison.Ordinal);

        return outside ? throw new WorkspacePathOutsideRootException(path, _root) : full;
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
