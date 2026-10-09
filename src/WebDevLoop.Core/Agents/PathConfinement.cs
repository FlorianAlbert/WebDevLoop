namespace WebDevLoop.Core.Agents;

/// <summary>
/// Directories an agent may read or write. Paths are normalized (relative to the working directory, <c>..</c> resolved)
/// before the containment check, so traversal and sibling-prefix tricks stay outside.
/// </summary>
public sealed class PathConfinement
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public PathConfinement(string workingDirectory, IReadOnlyList<string> readableRoots, IReadOnlyList<string> writableRoots)
    {
        WorkingDirectory = RequireAbsolute(workingDirectory, nameof(workingDirectory));
        ReadableRoots = readableRoots.Select(root => RequireAbsolute(root, nameof(readableRoots))).ToArray();
        WritableRoots = writableRoots.Select(root => RequireAbsolute(root, nameof(writableRoots))).ToArray();
    }

    public string WorkingDirectory { get; }

    public IReadOnlyList<string> ReadableRoots { get; }

    public IReadOnlyList<string> WritableRoots { get; }

    public bool CanRead(string path) => IsUnderAny(path, ReadableRoots) || CanWrite(path);

    public bool CanWrite(string path) => IsUnderAny(path, WritableRoots);

    internal static bool IsUnder(string path, string root)
    {
        string normalizedRoot = Normalize(root);
        string normalizedPath = Normalize(path);
        return string.Equals(normalizedPath, normalizedRoot, PathComparison)
            || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private bool IsUnderAny(string path, IReadOnlyList<string> roots)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string absolute = Path.GetFullPath(path, WorkingDirectory);
        return roots.Any(root => IsUnder(absolute, root));
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string RequireAbsolute(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"'{path}' must be an absolute path.", parameterName);
        }

        return Normalize(path);
    }
}
