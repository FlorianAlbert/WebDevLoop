using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

/// <summary>Result of merging into a worktree or creating a squash commit.</summary>
public sealed record GitMergeResult
{
    private GitMergeResult(GitMergeOutcome outcome, CommitSha? commit, IReadOnlyList<string> conflictedPaths)
    {
        Outcome = outcome;
        Commit = commit;
        ConflictedPaths = conflictedPaths;
    }

    public GitMergeOutcome Outcome { get; }

    /// <summary>The resulting commit; null only when <see cref="Outcome"/> is <see cref="GitMergeOutcome.Conflicted"/>.</summary>
    public CommitSha? Commit { get; }

    public IReadOnlyList<string> ConflictedPaths { get; }

    public static GitMergeResult Merged(CommitSha commit) => new(GitMergeOutcome.Merged, RequireCommit(commit), []);

    public static GitMergeResult AlreadyUpToDate(CommitSha head) => new(GitMergeOutcome.AlreadyUpToDate, RequireCommit(head), []);

    public static GitMergeResult Conflicted(IReadOnlyList<string> conflictedPaths)
    {
        ArgumentNullException.ThrowIfNull(conflictedPaths);
        if (conflictedPaths.Count == 0)
        {
            throw new ArgumentException("A conflicted merge must name at least one conflicted path.", nameof(conflictedPaths));
        }

        return new(GitMergeOutcome.Conflicted, null, conflictedPaths);
    }

    private static CommitSha RequireCommit(CommitSha commit) =>
        commit.Value is null ? throw new ArgumentException("A commit SHA is required.", nameof(commit)) : commit;
}
