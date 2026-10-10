namespace WebDevLoop.Core.Ports;

/// <param name="RemovedPaths">Untracked and ignored paths deleted from the worktree, relative to its root.</param>
public sealed record WorktreeCleanResult(IReadOnlyList<string> RemovedPaths);
