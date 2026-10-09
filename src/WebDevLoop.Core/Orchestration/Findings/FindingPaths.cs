namespace WebDevLoop.Core.Orchestration.Findings;

internal static class FindingPaths
{
    /// <summary>Repository-relative path with forward slashes and without a leading <c>./</c>.</summary>
    public static string Normalize(string file)
    {
        string path = file.Trim().Replace('\\', '/');
        return path.StartsWith("./", StringComparison.Ordinal) ? path[2..] : path;
    }
}
