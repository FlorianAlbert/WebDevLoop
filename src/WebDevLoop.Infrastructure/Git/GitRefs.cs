using LibGit2Sharp;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Git;

/// <summary>Ref and commit-graph queries. Callers hold the per-repository lock, which makes compare-and-swap atomic.</summary>
internal static class GitRefs
{
    private const string RefLogMessage = "webdevloop: compare-and-swap update";

    public static string Canonical(BranchName branch, GitRefScope scope = GitRefScope.Local) => scope switch
    {
        GitRefScope.Local => $"refs/heads/{branch}",
        GitRefScope.Remote => $"refs/remotes/{GitRemoteSync.RemoteName}/{branch}",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
    };

    public static CommitSha? Tip(Repository repo, string canonicalName) =>
        repo.Refs[canonicalName]?.ResolveToDirectReference()?.TargetIdentifier is { } sha ? new CommitSha(sha) : null;

    public static CommitSha ToSha(Commit commit) => new(commit.Sha);

    public static Commit RequireCommit(Repository repo, CommitSha sha) =>
        repo.Lookup<Commit>(sha.Value) ?? throw new InvalidOperationException($"Commit {sha} does not exist in '{repo.Info.Path}'.");

    public static RefUpdateResult CompareAndSwap(Repository repo, string canonicalName, CommitSha newTip, CommitSha? expectedPriorTip)
    {
        Commit target = RequireCommit(repo, newTip);
        CommitSha? current = Tip(repo, canonicalName);
        if (current == newTip)
        {
            return new RefUpdateResult(RefUpdateOutcome.AlreadyAtTarget, current);
        }

        if (current != expectedPriorTip)
        {
            return new RefUpdateResult(RefUpdateOutcome.ExpectedPriorMismatch, current);
        }

        if (current is null)
        {
            repo.Refs.Add(canonicalName, target.Id, RefLogMessage, allowOverwrite: false);
        }
        else
        {
            repo.Refs.UpdateTarget(repo.Refs[canonicalName], target.Id, RefLogMessage);
        }

        return new RefUpdateResult(RefUpdateOutcome.Updated, newTip);
    }

    public static bool IsAncestor(Repository repo, CommitSha ancestor, CommitSha descendant)
    {
        Commit? older = repo.Lookup<Commit>(ancestor.Value);
        Commit? newer = repo.Lookup<Commit>(descendant.Value);
        if (older is null || newer is null)
        {
            return false;
        }

        return older.Id == newer.Id || repo.ObjectDatabase.FindMergeBase(older, newer)?.Id == older.Id;
    }

    public static CommitSha? MergeBase(Repository repo, CommitSha first, CommitSha second)
    {
        Commit? left = repo.Lookup<Commit>(first.Value);
        Commit? right = repo.Lookup<Commit>(second.Value);
        return left is null || right is null || repo.ObjectDatabase.FindMergeBase(left, right) is not { } common ? null : ToSha(common);
    }

    public static IReadOnlyList<string> ChangedFiles(Repository repo, CommitSha from, CommitSha to)
    {
        TreeChanges changes = repo.Diff.Compare<TreeChanges>(RequireCommit(repo, from).Tree, RequireCommit(repo, to).Tree);
        return [.. changes.SelectMany(change => new[] { change.OldPath, change.Path }).Distinct().Order(StringComparer.Ordinal)];
    }

    public static IReadOnlyList<string> RecentCommits(Repository repo, CommitSha tip, int count) =>
    [
        .. repo.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = RequireCommit(repo, tip), SortBy = CommitSortStrategies.Topological })
            .Take(count)
            .Select(commit => $"{commit.Sha[..8]} {commit.MessageShort}"),
    ];
}
