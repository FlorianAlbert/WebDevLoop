namespace WebDevLoop.Core.Ports;

/// <summary>GitHub App permission set requested for a token; equal sets share a cache entry regardless of declaration order.</summary>
public sealed class GitHubPermissionSet : IEquatable<GitHubPermissionSet>
{
    private readonly SortedDictionary<string, GitHubPermissionLevel> _permissions;

    private GitHubPermissionSet(SortedDictionary<string, GitHubPermissionLevel> permissions)
    {
        _permissions = permissions;
    }

    public static GitHubPermissionSet Empty { get; } = new(new SortedDictionary<string, GitHubPermissionLevel>(StringComparer.Ordinal));

    public static GitHubPermissionSet ContentsRead => Empty.With("contents", GitHubPermissionLevel.Read);

    public static GitHubPermissionSet ContentsWrite => Empty.With("contents", GitHubPermissionLevel.Write);

    public static GitHubPermissionSet IssuesWrite => Empty.With("issues", GitHubPermissionLevel.Write);

    public static GitHubPermissionSet PullRequestsWrite => Empty.With("pull_requests", GitHubPermissionLevel.Write);

    public static GitHubPermissionSet CopilotRequests => Empty.With("copilot_requests", GitHubPermissionLevel.Write);

    public IReadOnlyDictionary<string, GitHubPermissionLevel> Permissions => _permissions;

    public GitHubPermissionSet With(string permission, GitHubPermissionLevel level)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        var permissions = new SortedDictionary<string, GitHubPermissionLevel>(_permissions, StringComparer.Ordinal)
        {
            [permission] = level,
        };
        return new GitHubPermissionSet(permissions);
    }

    public bool Equals(GitHubPermissionSet? other) => other is not null && _permissions.SequenceEqual(other._permissions);

    public override bool Equals(object? obj) => Equals(obj as GitHubPermissionSet);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach ((string permission, GitHubPermissionLevel level) in _permissions)
        {
            hash.Add(permission);
            hash.Add(level);
        }

        return hash.ToHashCode();
    }

    /// <summary>Stable form such as <c>contents:read,issues:write</c>, usable as a cache key.</summary>
    public override string ToString() =>
        string.Join(',', _permissions.Select(entry => $"{entry.Key}:{entry.Value.ToString().ToLowerInvariant()}"));
}
