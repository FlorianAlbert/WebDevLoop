using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.GitHub.Pulls;

/// <summary>A pull request already exists for the exact head ref but belongs to different run/ticket identifiers.</summary>
public sealed class PullRequestConflictException(BranchName head, PullRequestNumber existing)
    : Exception($"Pull request #{existing} already exists for head '{head}' but carries different run/ticket identifiers.")
{
    public BranchName Head { get; } = head;

    public PullRequestNumber Existing { get; } = existing;
}
