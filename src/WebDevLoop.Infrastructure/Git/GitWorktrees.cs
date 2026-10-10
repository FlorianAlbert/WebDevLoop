using System.Security.Cryptography;
using System.Text;
using LibGit2Sharp;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Git;

/// <summary>Linked worktrees of an app-owned clone. Paths passed in are already confined and normalized.</summary>
internal static class GitWorktrees
{
    private const string WorktreeNamePrefix = "wdl-";
    private const int WorktreeNameHashLength = 16;
    private const string WorktreesMetadataDirectory = "worktrees";
    private const string LockedMarkerFile = "locked";

    public static TicketWorktree Prepare(Repository repo, BranchName branch, CommitSha startPoint, string path)
    {
        Commit start = GitRefs.RequireCommit(repo, startPoint);
        if (Find(repo, path) is { } existing)
        {
            ResetExisting(existing, branch, start);
            return new TicketWorktree(path, branch, startPoint);
        }

        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
        {
            throw new InvalidOperationException($"'{path}' already exists and is not a worktree of '{repo.Info.WorkingDirectory}'.");
        }

        PointBranchAt(repo, branch, start);
        Add(repo, branch, path);
        return new TicketWorktree(path, branch, startPoint);
    }

    public static WorktreeInspection Inspect(Repository repo, string path)
    {
        if (Find(repo, path) is not { } worktree)
        {
            return new WorktreeInspection(WorktreeStatus.Missing, null, null);
        }

        using Repository linked = worktree.WorktreeRepository;
        BranchName? branch = linked.Info.IsHeadDetached ? null : new BranchName(linked.Head.FriendlyName);
        CommitSha? head = linked.Head.Tip is { } tip ? GitRefs.ToSha(tip) : null;
        return new WorktreeInspection(StatusOf(worktree, linked), branch, head);
    }

    public static WorktreeChanges Changes(Repository repo, string path)
    {
        if (Find(repo, path) is not { } worktree)
        {
            return new WorktreeChanges(string.Empty, [], [], []);
        }

        using Repository linked = worktree.WorktreeRepository;
        var tracked = new List<string>();
        var untracked = new List<string>();
        var ignored = new List<string>();
        foreach (StatusEntry entry in linked.RetrieveStatus(FullStatus))
        {
            List<string> bucket = entry.State switch
            {
                FileStatus.Ignored => ignored,
                FileStatus.NewInWorkdir => untracked,
                _ => tracked,
            };
            bucket.Add(entry.FilePath);
        }

        string patch = tracked.Count > 0 && linked.Head.Tip is { } tip
            ? linked.Diff.Compare<Patch>(tip.Tree, DiffTargets.Index | DiffTargets.WorkingDirectory).Content
            : string.Empty;
        return new WorktreeChanges(patch, tracked, untracked, ignored);
    }

    /// <summary>Hard-resets tracked files, then deletes untracked and ignored files (<c>git clean -fdx</c>) inside this worktree only.</summary>
    public static WorktreeCleanResult Clean(Repository repo, string path)
    {
        if (Find(repo, path) is not { } worktree)
        {
            return new WorktreeCleanResult([]);
        }

        using Repository linked = worktree.WorktreeRepository;
        if (linked.Head.Tip is { } tip)
        {
            linked.Reset(ResetMode.Hard, tip);
        }

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var removed = new List<string>();
        foreach (StatusEntry entry in linked.RetrieveStatus(FullStatus).Where(e => e.State is FileStatus.Ignored or FileStatus.NewInWorkdir))
        {
            string full = Path.GetFullPath(Path.Combine(root, entry.FilePath));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            if (Directory.Exists(full) && !IsSymbolicLink(full))
            {
                // A nested repository is somebody else's checkout, like git clean without a second -f.
                if (!Directory.Exists(Path.Combine(full, ".git")) && !File.Exists(Path.Combine(full, ".git")))
                {
                    Directory.Delete(full, recursive: true);
                    removed.Add(entry.FilePath);
                }
            }
            else if (File.Exists(full) || IsSymbolicLink(full))
            {
                File.Delete(full);
                RemoveEmptyParents(full, root);
                removed.Add(entry.FilePath);
            }
        }

        return new WorktreeCleanResult(removed);
    }

    private static readonly StatusOptions FullStatus = new()
    {
        IncludeUntracked = true,
        IncludeIgnored = true,
        RecurseUntrackedDirs = true,
        RecurseIgnoredDirs = true,
    };

    private static bool IsSymbolicLink(string path) => new FileInfo(path).LinkTarget is not null;

    private static void RemoveEmptyParents(string file, string root)
    {
        for (string? dir = Path.GetDirectoryName(file); dir is not null && dir != root && !Directory.EnumerateFileSystemEntries(dir).Any(); dir = Path.GetDirectoryName(dir))
        {
            Directory.Delete(dir);
        }
    }

    /// <summary>Removes a clean, unlocked worktree directory and its metadata. Branches and remote refs are never deleted.</summary>
    public static WorktreeCleanupResult Cleanup(Repository repo, string path)
    {
        string name = NameFor(path);
        if (!Directory.Exists(path))
        {
            return IsLockedMetadata(repo, name)
                ? new WorktreeCleanupResult(WorktreeCleanupOutcome.AlreadyMissing, $"Worktree '{path}' is gone but its metadata is locked; metadata left in place.")
                : PrunedMissing(repo, name);
        }

        if (Find(repo, path) is not { } worktree)
        {
            return new WorktreeCleanupResult(WorktreeCleanupOutcome.AlreadyMissing, $"'{path}' is not a worktree of '{repo.Info.WorkingDirectory}'; left untouched.");
        }

        using (Repository linked = worktree.WorktreeRepository)
        {
            if (IsDirty(linked))
            {
                string locked = worktree.IsLocked ? " and is locked" : string.Empty;
                return new WorktreeCleanupResult(WorktreeCleanupOutcome.RetainedDirty, $"Worktree '{path}' has uncommitted changes{locked}; left in place.");
            }
        }

        if (worktree.IsLocked)
        {
            return new WorktreeCleanupResult(WorktreeCleanupOutcome.RetainedLocked, $"Worktree '{path}' is locked; left in place.");
        }

        repo.Worktrees.Prune(worktree, ifLocked: false);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        return new WorktreeCleanupResult(WorktreeCleanupOutcome.Removed);
    }

    /// <summary>The registered, valid worktree whose working directory is <paramref name="path"/>; null when missing.</summary>
    public static Worktree? Find(Repository repo, string path) =>
        Directory.Exists(path) ? repo.Worktrees[NameFor(path)] : null;

    public static bool IsDirty(Repository linked) => linked.RetrieveStatus(new StatusOptions { IncludeIgnored = false }).IsDirty;

    /// <summary>Deterministic, single-segment worktree name; branch names contain slashes, which libgit2 rejects as names.</summary>
    public static string NameFor(string path) =>
        WorktreeNamePrefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..WorktreeNameHashLength];

    private static WorktreeStatus StatusOf(Worktree worktree, Repository linked) =>
        IsDirty(linked) ? WorktreeStatus.Dirty
        : worktree.IsLocked ? WorktreeStatus.Locked
        : WorktreeStatus.Clean;

    private static void ResetExisting(Worktree worktree, BranchName branch, Commit start)
    {
        using Repository linked = worktree.WorktreeRepository;
        if (linked.Info.IsHeadDetached || linked.Head.CanonicalName != GitRefs.Canonical(branch))
        {
            throw new InvalidOperationException($"Worktree '{linked.Info.WorkingDirectory}' is on '{linked.Head.FriendlyName}', not '{branch}'.");
        }

        linked.Reset(ResetMode.Hard, start);
        linked.RemoveUntrackedFiles();
    }

    private static void PointBranchAt(Repository repo, BranchName branch, Commit start)
    {
        string canonical = GitRefs.Canonical(branch);
        if (repo.Refs[canonical] is { } existing)
        {
            repo.Refs.UpdateTarget(existing, start.Id, "webdevloop: reset ticket branch");
        }
        else
        {
            repo.Refs.Add(canonical, start.Id, "webdevloop: create ticket branch", allowOverwrite: false);
        }
    }

    private static void Add(Repository repo, BranchName branch, string path)
    {
        string name = NameFor(path);
        PruneStaleMetadata(repo, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // libgit2 always creates a branch named after the worktree at the main HEAD before LibGit2Sharp switches the
        // worktree to the requested branch; that helper branch is deleted again once it is no longer checked out.
        repo.Worktrees.Add(branch.Value, name, path, isLocked: false);
        repo.Branches.Remove(name);
    }

    private static WorktreeCleanupResult PrunedMissing(Repository repo, string name)
    {
        PruneStaleMetadata(repo, name);
        return new WorktreeCleanupResult(WorktreeCleanupOutcome.AlreadyMissing);
    }

    private static bool IsLockedMetadata(Repository repo, string name) =>
        File.Exists(Path.Combine(MetadataPath(repo, name), LockedMarkerFile));

    private static string MetadataPath(Repository repo, string name) => Path.Combine(repo.Info.Path, WorktreesMetadataDirectory, name);

    /// <summary>Removes leftover metadata for a worktree whose directory is gone (what <c>git worktree prune</c> does).</summary>
    public static void PruneStaleMetadata(Repository repo, string name)
    {
        string metadata = MetadataPath(repo, name);
        if (Directory.Exists(metadata) && repo.Worktrees[name] is null)
        {
            Directory.Delete(metadata, recursive: true);
        }
    }
}
