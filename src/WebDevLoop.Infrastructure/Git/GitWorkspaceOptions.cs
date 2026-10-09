namespace WebDevLoop.Infrastructure.Git;

public sealed class GitWorkspaceOptions
{
    public const string DefaultCommitterName = "WebDevLoop";
    public const string DefaultCommitterEmail = "webdevloop@users.noreply.github.com";

    /// <summary>Every clone and worktree path must resolve to a location under this directory.</summary>
    public required string WorkspaceRoot { get; init; }

    public string CommitterName { get; init; } = DefaultCommitterName;

    public string CommitterEmail { get; init; } = DefaultCommitterEmail;
}
