namespace WebDevLoop.Core.Agents;

public enum GitHubTokenAccess
{
    /// <summary>The session receives no GitHub token (Copilot auth only).</summary>
    None,

    /// <summary>A read-only token may be supplied when private reads are unavoidable.</summary>
    ReadOnlyIfRequired,

    Write,
}
