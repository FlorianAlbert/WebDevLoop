namespace WebDevLoop.Infrastructure.Git;

public sealed class GitWorkspaceOptions
{
    public const string DefaultCommitterName = "WebDevLoop";
    public const string DefaultCommitterEmail = "webdevloop@users.noreply.github.com";

    /// <summary>Every clone and worktree path must resolve to a location under this directory.</summary>
    public required string WorkspaceRoot { get; init; }

    public string CommitterName { get; init; } = DefaultCommitterName;

    public string CommitterEmail { get; init; } = DefaultCommitterEmail;

    /// <summary>Let clone, fetch and push use the configured PAT when the GitHub App cannot act (still requires PAT fallback to be enabled in the token provider).</summary>
    public bool AllowUserTokenFallback { get; init; } = true;
}
