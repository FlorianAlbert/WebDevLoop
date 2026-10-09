using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.ReviewLoop;

/// <summary>What the reviewers inspect: a checkout they may only read, and the commit range under review.</summary>
/// <param name="WorkingDirectory">Read-only checkout of <paramref name="Branch"/> (ticket worktree or integration checkout).</param>
/// <param name="DiffBase">Fixed point the changes are diffed against; an ancestor of <paramref name="DiffHead"/>.</param>
/// <param name="DiffHead">The reviewed commit; review results are only valid for exactly this commit.</param>
public sealed record ReviewTarget(string WorkingDirectory, BranchName Branch, CommitSha DiffBase, CommitSha DiffHead);
