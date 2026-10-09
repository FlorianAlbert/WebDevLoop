using LibGit2Sharp;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Git;

internal static class GitMerges
{
    /// <summary>In-memory merge of <see cref="SquashRequest.Source"/> onto the integration tip, committed with a single parent. No ref moves.</summary>
    public static GitMergeResult Squash(Repository repo, SquashRequest request, Signature signature)
    {
        Commit source = GitRefs.RequireCommit(repo, request.Source);
        Commit integrationTip = GitRefs.RequireCommit(repo, request.IntegrationTip);
        if (GitRefs.IsAncestor(repo, request.Source, request.IntegrationTip))
        {
            return GitMergeResult.AlreadyUpToDate(request.IntegrationTip);
        }

        MergeTreeResult merge = repo.ObjectDatabase.MergeCommits(integrationTip, source, new MergeTreeOptions());
        switch (merge.Status)
        {
            case MergeTreeStatus.Conflicts:
                return GitMergeResult.Conflicted(ConflictPaths(merge.Conflicts));
            case MergeTreeStatus.Succeeded when merge.Tree.Id == integrationTip.Tree.Id:
                return GitMergeResult.AlreadyUpToDate(request.IntegrationTip);
            case MergeTreeStatus.Succeeded:
                Commit squash = repo.ObjectDatabase.CreateCommit(
                    signature, signature, request.Message, merge.Tree, [integrationTip], prettifyMessage: false);
                return GitMergeResult.Merged(GitRefs.ToSha(squash));
            default:
                throw new InvalidOperationException($"Squash merge of {request.Source} onto {request.IntegrationTip} failed: {merge.Status}.");
        }
    }

    /// <summary>Merges <paramref name="source"/> into the worktree's checked-out branch. Conflicts stay in the worktree for the resolver.</summary>
    public static GitMergeResult MergeIntoWorktree(Repository worktree, BranchName branch, CommitSha source, string message, Signature signature)
    {
        if (worktree.Info.IsHeadDetached || worktree.Head.CanonicalName != GitRefs.Canonical(branch))
        {
            throw new InvalidOperationException($"Worktree '{worktree.Info.WorkingDirectory}' is not on branch '{branch}'.");
        }

        Commit incoming = GitRefs.RequireCommit(worktree, source);
        Commit head = worktree.Head.Tip;
        if (GitRefs.IsAncestor(worktree, source, GitRefs.ToSha(head)))
        {
            return GitMergeResult.AlreadyUpToDate(GitRefs.ToSha(head));
        }

        MergeResult result = worktree.Merge(incoming, signature, new MergeOptions { CommitOnSuccess = false });
        return result.Status switch
        {
            MergeStatus.FastForward => GitMergeResult.Merged(source),
            MergeStatus.NonFastForward => GitMergeResult.Merged(GitRefs.ToSha(CommitMerge(worktree, message, signature))),
            MergeStatus.Conflicts => GitMergeResult.Conflicted(ConflictPaths(worktree.Index.Conflicts)),
            _ => GitMergeResult.AlreadyUpToDate(GitRefs.ToSha(head)),
        };
    }

    private static Commit CommitMerge(Repository worktree, string message, Signature signature) =>
        worktree.Commit(message, signature, signature, new CommitOptions { PrettifyMessage = false });

    private static IReadOnlyList<string> ConflictPaths(IEnumerable<Conflict> conflicts) =>
        [.. conflicts.Select(c => c.Ours?.Path ?? c.Theirs?.Path ?? c.Ancestor.Path).Distinct().Order(StringComparer.Ordinal)];
}
