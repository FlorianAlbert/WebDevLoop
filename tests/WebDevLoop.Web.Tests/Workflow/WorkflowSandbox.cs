using LibGit2Sharp;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Web.Tests.Workflow;

/// <summary>
/// A throw-away app data directory and a local bare "origin" repository standing in for GitHub's git remote. It lives under
/// the test output directory and is deleted on dispose.
/// </summary>
internal sealed class WorkflowSandbox : IDisposable
{
    public static readonly BranchName Trunk = new("main");
    private static readonly Signature Seeder = new("Seeder", "seeder@example.invalid", new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));

    public WorkflowSandbox()
    {
        Root = Path.Combine(AppContext.BaseDirectory, "workflow-sandboxes", Guid.NewGuid().ToString("N"));
        DataDirectory = Path.Combine(Root, "data");
        RemotePath = Path.Combine(Root, "origin.git");
        Directory.CreateDirectory(DataDirectory);
        Repository.Init(RemotePath, isBare: true);
        using var remote = new Repository(RemotePath);
        remote.Refs.UpdateTarget("HEAD", $"refs/heads/{Trunk}");
        Tree tree = remote.ObjectDatabase.CreateTree(TreeWith(remote, "README.md", "widgets\n"));
        Commit initial = remote.ObjectDatabase.CreateCommit(Seeder, Seeder, "Initial commit", tree, [], prettifyMessage: false);
        remote.Refs.Add($"refs/heads/{Trunk}", initial.Id);
    }

    public string Root { get; }

    public string DataDirectory { get; }

    public string RemotePath { get; }

    public CommitSha? RemoteTip(BranchName branch)
    {
        using var remote = new Repository(RemotePath);
        return remote.Branches[branch.Value]?.Tip is { } tip ? new CommitSha(tip.Sha) : null;
    }

    public IReadOnlyList<string> FilesOnTrunk()
    {
        using var remote = new Repository(RemotePath);
        return [.. remote.Branches[Trunk.Value].Tip.Tree.Select(entry => entry.Path).Order(StringComparer.Ordinal)];
    }

    /// <summary>What GitHub does when a human merges a PR stack: trunk moves to the top layer.</summary>
    public void FastForwardTrunk(CommitSha top)
    {
        using var remote = new Repository(RemotePath);
        Commit commit = remote.Lookup<Commit>(top.Value);
        Commit trunk = remote.Branches[Trunk.Value].Tip;
        if (!remote.ObjectDatabase.FindMergeBase(trunk, commit)!.Sha.Equals(trunk.Sha, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Trunk {trunk.Sha} cannot fast-forward to {top}.");
        }

        remote.Refs.UpdateTarget($"refs/heads/{Trunk}", commit.Id.Sha);
    }

    public void Dispose()
    {
        if (!Directory.Exists(Root))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(Root, recursive: true);
    }

    private static TreeDefinition TreeWith(Repository repository, string path, string content)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        var tree = new TreeDefinition();
        tree.Add(path, repository.ObjectDatabase.CreateBlob(stream), Mode.NonExecutableFile);
        return tree;
    }
}
