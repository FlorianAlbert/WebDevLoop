using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Git;

namespace WebDevLoop.Infrastructure.Tests.Git;

public sealed class GitWorkspaceRemediationTests : IAsyncLifetime
{
    private static readonly BranchName TicketBranch = RunScopedNaming.TicketBranch(new RunId("run-1"), new TicketRunId("t-1"));
    private readonly GitSandbox _sandbox = new();
    private readonly GitWorkspace _workspace;
    private TicketWorktree _worktree = null!;

    public GitWorkspaceRemediationTests() => _workspace = _sandbox.CreateWorkspace();

    public async ValueTask InitializeAsync()
    {
        await _workspace.EnsureClonedAsync(_sandbox.Location, CancellationToken.None);
        _worktree = await _workspace.PrepareWorktreeAsync(
            _sandbox.Location, new WorktreeSpec(TicketBranch, _sandbox.InitialCommit, _sandbox.WorktreePath("t-1")), CancellationToken.None);
    }

    [Fact]
    public async Task an_untracked_artefact_makes_the_worktree_dirty_and_cleaning_makes_it_clean_again()
    {
        string cache = Path.Combine(_worktree.Path, "__pycache__", "calc.cpython-312.pyc");
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        File.WriteAllText(cache, "bytecode");
        Assert.Equal(WorktreeStatus.Dirty, await StatusAsync());

        WorktreeChanges changes = await _workspace.GetWorktreeChangesAsync(_sandbox.Location, _worktree.Path, CancellationToken.None);
        WorktreeCleanResult cleaned = await _workspace.CleanWorktreeAsync(_sandbox.Location, _worktree.Path, CancellationToken.None);

        Assert.False(changes.HasTrackedChanges);
        Assert.Equal(["__pycache__/calc.cpython-312.pyc"], changes.UntrackedFiles);
        Assert.Equal(["__pycache__/calc.cpython-312.pyc"], cleaned.RemovedPaths);
        Assert.False(Directory.Exists(Path.Combine(_worktree.Path, "__pycache__")));
        Assert.Equal(WorktreeStatus.Clean, await StatusAsync());
    }

    [Fact]
    public async Task ignored_files_are_removed_too_but_do_not_make_the_worktree_dirty()
    {
        CommitSha head = GitSandbox.CommitInWorktree(_worktree.Path, new Dictionary<string, string> { [".gitignore"] = "__pycache__/\n*.log\n" }, "ignore artefacts");
        string cache = Path.Combine(_worktree.Path, "__pycache__", "calc.pyc");
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        File.WriteAllText(cache, "bytecode");
        File.WriteAllText(Path.Combine(_worktree.Path, "run.log"), "log");
        string untracked = Path.Combine(_worktree.Path, "notes.txt");
        File.WriteAllText(untracked, "left behind");
        Assert.Equal(WorktreeStatus.Dirty, await StatusAsync());

        WorktreeChanges changes = await _workspace.GetWorktreeChangesAsync(_sandbox.Location, _worktree.Path, CancellationToken.None);
        WorktreeCleanResult cleaned = await _workspace.CleanWorktreeAsync(_sandbox.Location, _worktree.Path, CancellationToken.None);

        Assert.Equal(["notes.txt"], changes.UntrackedFiles);
        Assert.Equal(["__pycache__/calc.pyc", "run.log"], changes.IgnoredFiles.Order());
        Assert.Equal(["__pycache__/calc.pyc", "notes.txt", "run.log"], cleaned.RemovedPaths.Order());
        Assert.False(File.Exists(untracked));
        Assert.False(File.Exists(Path.Combine(_worktree.Path, "run.log")));
        Assert.True(File.Exists(Path.Combine(_worktree.Path, ".gitignore")));
        Assert.Equal(head, (await _workspace.InspectWorktreeAsync(_sandbox.Location, _worktree.Path, CancellationToken.None)).Head);
        Assert.Equal(WorktreeStatus.Clean, await StatusAsync());
    }

    [Fact]
    public async Task modified_and_staged_tracked_changes_are_reported_as_a_patch_and_reset()
    {
        CommitSha head = GitSandbox.CommitInWorktree(_worktree.Path, new Dictionary<string, string> { ["calc.py"] = "def add(a, b):\n    return a + b\n" }, "add calc");
        string calc = Path.Combine(_worktree.Path, "calc.py");
        File.WriteAllText(calc, "def add(a, b):\n    return a - b\n");

        WorktreeChanges changes = await _workspace.GetWorktreeChangesAsync(_sandbox.Location, _worktree.Path, CancellationToken.None);
        await _workspace.CleanWorktreeAsync(_sandbox.Location, _worktree.Path, CancellationToken.None);

        Assert.Equal(["calc.py"], changes.TrackedFiles);
        Assert.Contains("-    return a + b", changes.TrackedPatch, StringComparison.Ordinal);
        Assert.Contains("+    return a - b", changes.TrackedPatch, StringComparison.Ordinal);
        Assert.Equal("def add(a, b):\n    return a + b\n", File.ReadAllText(calc));
        Assert.Equal(head, (await _workspace.InspectWorktreeAsync(_sandbox.Location, _worktree.Path, CancellationToken.None)).Head);
        Assert.Equal(WorktreeStatus.Clean, await StatusAsync());
    }

    [Fact]
    public async Task cleaning_one_worktree_leaves_the_clone_and_other_worktrees_untouched()
    {
        TicketWorktree other = await _workspace.PrepareWorktreeAsync(
            _sandbox.Location, new WorktreeSpec(RunScopedNaming.TicketBranch(new RunId("run-1"), new TicketRunId("t-2")), _sandbox.InitialCommit, _sandbox.WorktreePath("t-2")), CancellationToken.None);
        string otherScratch = Path.Combine(other.Path, "scratch.txt");
        string cloneScratch = Path.Combine(_sandbox.Location.LocalPath, "scratch.txt");
        File.WriteAllText(otherScratch, "keep");
        File.WriteAllText(cloneScratch, "keep");
        File.WriteAllText(Path.Combine(_worktree.Path, "scratch.txt"), "remove");

        await _workspace.CleanWorktreeAsync(_sandbox.Location, _worktree.Path, CancellationToken.None);

        Assert.True(File.Exists(otherScratch));
        Assert.True(File.Exists(cloneScratch));
        Assert.False(File.Exists(Path.Combine(_worktree.Path, "scratch.txt")));
    }

    [Fact]
    public async Task a_missing_worktree_has_no_changes_and_nothing_to_clean()
    {
        string missing = _sandbox.WorktreePath("nope");

        WorktreeChanges changes = await _workspace.GetWorktreeChangesAsync(_sandbox.Location, missing, CancellationToken.None);
        WorktreeCleanResult cleaned = await _workspace.CleanWorktreeAsync(_sandbox.Location, missing, CancellationToken.None);

        Assert.True(changes.IsEmpty);
        Assert.Empty(cleaned.RemovedPaths);
    }

    public ValueTask DisposeAsync()
    {
        _sandbox.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<WorktreeStatus> StatusAsync() =>
        (await _workspace.InspectWorktreeAsync(_sandbox.Location, _worktree.Path, CancellationToken.None)).Status;
}
