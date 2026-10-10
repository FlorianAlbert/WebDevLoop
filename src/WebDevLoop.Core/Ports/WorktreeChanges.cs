namespace WebDevLoop.Core.Ports;

/// <summary>What differs between a worktree and its checked-out commit, with paths relative to the worktree root.</summary>
/// <param name="TrackedPatch">Unified diff of staged and unstaged changes to tracked files; empty when there are none.</param>
/// <param name="TrackedFiles">Tracked files with staged or unstaged changes.</param>
/// <param name="UntrackedFiles">Files Git does not know and does not ignore.</param>
/// <param name="IgnoredFiles">Files that match an ignore rule.</param>
public sealed record WorktreeChanges(
    string TrackedPatch,
    IReadOnlyList<string> TrackedFiles,
    IReadOnlyList<string> UntrackedFiles,
    IReadOnlyList<string> IgnoredFiles)
{
    public bool HasTrackedChanges => TrackedFiles.Count > 0;

    public bool IsEmpty => !HasTrackedChanges && UntrackedFiles.Count == 0 && IgnoredFiles.Count == 0;
}
