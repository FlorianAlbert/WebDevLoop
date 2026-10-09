using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

/// <summary>Create (or reset) the worktree at <paramref name="Path"/> on <paramref name="Branch"/> starting at <paramref name="StartPoint"/>.</summary>
public sealed record WorktreeSpec(BranchName Branch, CommitSha StartPoint, string Path);
