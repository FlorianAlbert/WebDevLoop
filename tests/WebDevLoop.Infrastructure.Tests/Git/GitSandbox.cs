using LibGit2Sharp;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Git;

namespace WebDevLoop.Infrastructure.Tests.Git;

/// <summary>
/// A throw-away directory holding a bare "origin" repository and a workspace root. It lives under the test output
/// directory (not the system temp dir) and is deleted on dispose.
/// </summary>
internal sealed class GitSandbox : IDisposable
{
    public static readonly BranchName Main = new("main");
    public static readonly GitHubRepoRef Repo = new("acme", "widgets");
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Signature Author = new("Seeder", "seeder@example.invalid", Now);

    public GitSandbox()
    {
        Root = Path.Combine(AppContext.BaseDirectory, "git-sandboxes", Guid.NewGuid().ToString("N"));
        WorkspaceRoot = Path.Combine(Root, "workspace");
        RemotePath = Path.Combine(Root, "origin.git");
        Directory.CreateDirectory(WorkspaceRoot);
        Repository.Init(RemotePath, isBare: true);
        using var remote = new Repository(RemotePath);
        remote.Refs.UpdateTarget("HEAD", $"refs/heads/{Main}");
        InitialCommit = CommitToRemote(Main, new Dictionary<string, string> { ["README.md"] = "hello\n" }, "Initial commit");
    }

    public string Root { get; }

    public string WorkspaceRoot { get; }

    public string RemotePath { get; }

    public CommitSha InitialCommit { get; }

    public RecordingGitCredentialSource Credentials { get; } = new();

    public GitRepositoryLocation Location => new(Repo, RemotePath, Path.Combine(WorkspaceRoot, "clones", Repo.Owner, Repo.Name));

    public string WorktreePath(string name) => Path.Combine(WorkspaceRoot, "worktrees", name);

    public GitWorkspace CreateWorkspace() => CreateWorkspace(new GitWorkspaceOptions { WorkspaceRoot = WorkspaceRoot });

    public GitWorkspace CreateWorkspace(GitWorkspaceOptions options) => new(options, Credentials, new FixedClock(Now));

    /// <summary>Commits directly into the bare remote on top of the branch's current tip (or as a root commit).</summary>
    public CommitSha CommitToRemote(BranchName branch, IReadOnlyDictionary<string, string> files, string message)
    {
        using var remote = new Repository(RemotePath);
        Commit? parent = remote.Branches[branch.Value]?.Tip;
        TreeDefinition tree = parent is null ? new TreeDefinition() : TreeDefinition.From(parent);
        foreach ((string path, string content) in files)
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
            tree.Add(path, remote.ObjectDatabase.CreateBlob(stream), Mode.NonExecutableFile);
        }

        Commit commit = remote.ObjectDatabase.CreateCommit(
            Author, Author, message, remote.ObjectDatabase.CreateTree(tree), parent is null ? [] : [parent], prettifyMessage: false);
        remote.Refs.UpdateTarget(remote.Refs.Add($"refs/heads/{branch}", commit.Id, allowOverwrite: true), commit.Id);
        return new CommitSha(commit.Sha);
    }

    public CommitSha? RemoteTip(BranchName branch)
    {
        using var remote = new Repository(RemotePath);
        return remote.Branches[branch.Value]?.Tip is { } tip ? new CommitSha(tip.Sha) : null;
    }

    public static CommitSha CommitInWorktree(string worktreePath, IReadOnlyDictionary<string, string> files, string message)
    {
        using var repo = new Repository(worktreePath);
        foreach ((string path, string content) in files)
        {
            string fullPath = Path.Combine(worktreePath, path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        Commands.Stage(repo, "*");
        return new CommitSha(repo.Commit(message, Author, Author).Sha);
    }

    public static IReadOnlyList<string> ParentsOf(string repositoryPath, CommitSha commit)
    {
        using var repo = new Repository(repositoryPath);
        return [.. repo.Lookup<Commit>(commit.Value).Parents.Select(p => p.Sha)];
    }

    public static string MessageOf(string repositoryPath, CommitSha commit)
    {
        using var repo = new Repository(repositoryPath);
        return repo.Lookup<Commit>(commit.Value).Message;
    }

    public static string? ReadFileAt(string repositoryPath, CommitSha commit, string path)
    {
        using var repo = new Repository(repositoryPath);
        return repo.Lookup<Commit>(commit.Value)[path]?.Target is Blob blob ? blob.GetContentText() : null;
    }

    public static void LockWorktree(string clonePath, string worktreePath)
    {
        using var repo = new Repository(clonePath);
        Worktree worktree = repo.Worktrees.Where(w => w is not null).Single(w =>
        {
            using Repository linked = w.WorktreeRepository;
            return Path.TrimEndingDirectorySeparator(linked.Info.WorkingDirectory) == Path.GetFullPath(worktreePath);
        });
        worktree.Lock("held by test");
    }

    public static IReadOnlyList<string> LocalBranches(string clonePath)
    {
        using var repo = new Repository(clonePath);
        return [.. repo.Branches.Where(b => !b.IsRemote).Select(b => b.FriendlyName).Order(StringComparer.Ordinal)];
    }

    public void Dispose()
    {
        if (!Directory.Exists(Root))
        {
            return;
        }

        // Git marks pack and object files read-only; clear that so deletion also works on Windows.
        foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(Root, recursive: true);
    }
}
