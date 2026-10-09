using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Git;

namespace WebDevLoop.Infrastructure.Tests.Git;

public sealed class GitWorkspaceCleanupTests : IAsyncLifetime
{
    private static readonly RunId Run = new("run-1");
    private static readonly TicketRunId Ticket = new("t-1");
    private static readonly BranchName TicketBranch = RunScopedNaming.TicketBranch(Run, Ticket);
    private readonly GitSandbox _sandbox = new();
    private readonly GitWorkspace _workspace;
    private TicketWorktree _worktree = null!;

    public GitWorkspaceCleanupTests() => _workspace = _sandbox.CreateWorkspace();

    public async ValueTask InitializeAsync()
    {
        await _workspace.EnsureClonedAsync(_sandbox.Location, CancellationToken.None);
        _worktree = await _workspace.PrepareWorktreeAsync(
            _sandbox.Location, new WorktreeSpec(TicketBranch, _sandbox.InitialCommit, _sandbox.WorktreePath("t-1")), CancellationToken.None);
    }

    [Fact]
    public async Task clean_worktree_is_removed_without_deleting_branches_or_open_pr_refs()
    {
        CommitSha tip = GitSandbox.CommitInWorktree(_worktree.Path, new Dictionary<string, string> { ["a.txt"] = "a" }, "a");
        BranchName stack = RunScopedNaming.StackBranch(Run, Ticket);
        await _workspace.PushAsync(_sandbox.Location, new RefPush(stack, tip, null), CancellationToken.None);

        WorktreeCleanupResult result = await _workspace.CleanupWorktreeAsync(_sandbox.Location, _worktree.Path, CancellationToken.None);

        Assert.Equal(new WorktreeCleanupResult(WorktreeCleanupOutcome.Removed), result);
        Assert.False(Directory.Exists(_worktree.Path));
        Assert.Equal(WorktreeStatus.Missing, (await _workspace.InspectWorktreeAsync(_sandbox.Location, _worktree.Path, CancellationToken.None)).Status);
        Assert.Equal(tip, await _workspace.GetBranchTipAsync(_sandbox.Location, TicketBranch, GitRefScope.Local, CancellationToken.None));
        Assert.Equal(tip, _sandbox.RemoteTip(stack));
    }

    [Fact]
    public async Task dirty_worktree_cleanup_records_a_warning_and_leaves_it_in_place()
    {
        string scratch = Path.Combine(_worktree.Path, "work-in-progress.txt");
        File.WriteAllText(scratch, "unsaved");

        WorktreeCleanupResult result = await _workspace.CleanupWorktreeAsync(_sandbox.Location, _worktree.Path, CancellationToken.None);

        Assert.Equal(WorktreeCleanupOutcome.RetainedDirty, result.Outcome);
        Assert.Contains(_worktree.Path, result.Warning);
        Assert.True(File.Exists(scratch));
    }

    [Fact]
    public async Task locked_worktree_cleanup_records_a_warning_and_leaves_it_in_place()
    {
        GitSandbox.LockWorktree(_sandbox.Location.LocalPath, _worktree.Path);

        WorktreeCleanupResult result = await _workspace.CleanupWorktreeAsync(_sandbox.Location, _worktree.Path, CancellationToken.None);

        Assert.Equal(WorktreeCleanupOutcome.RetainedLocked, result.Outcome);
        Assert.Contains("locked", result.Warning);
        Assert.Equal(WorktreeStatus.Locked, (await _workspace.InspectWorktreeAsync(_sandbox.Location, _worktree.Path, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task dirty_locked_worktree_cleanup_records_a_warning_and_leaves_it_in_place()
    {
        string scratch = Path.Combine(_worktree.Path, "work-in-progress.txt");
        File.WriteAllText(scratch, "unsaved");
        GitSandbox.LockWorktree(_sandbox.Location.LocalPath, _worktree.Path);

        WorktreeCleanupResult result = await _workspace.CleanupWorktreeAsync(_sandbox.Location, _worktree.Path, CancellationToken.None);

        Assert.Equal(WorktreeCleanupOutcome.RetainedDirty, result.Outcome);
        Assert.Contains("uncommitted", result.Warning);
        Assert.Contains("locked", result.Warning);
        Assert.True(File.Exists(scratch));
    }

    [Fact]
    public async Task worktree_whose_directory_vanished_is_pruned_and_reported_missing()
    {
        Directory.Delete(_worktree.Path, recursive: true);

        WorktreeCleanupResult result = await _workspace.CleanupWorktreeAsync(_sandbox.Location, _worktree.Path, CancellationToken.None);
        TicketWorktree recreated = await _workspace.PrepareWorktreeAsync(
            _sandbox.Location, new WorktreeSpec(TicketBranch, _sandbox.InitialCommit, _worktree.Path), CancellationToken.None);

        Assert.Equal(new WorktreeCleanupResult(WorktreeCleanupOutcome.AlreadyMissing), result);
        Assert.Equal(WorktreeStatus.Clean, (await _workspace.InspectWorktreeAsync(_sandbox.Location, recreated.Path, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task unknown_path_is_already_missing()
    {
        WorktreeCleanupResult result = await _workspace.CleanupWorktreeAsync(_sandbox.Location, _sandbox.WorktreePath("never"), CancellationToken.None);

        Assert.Equal(new WorktreeCleanupResult(WorktreeCleanupOutcome.AlreadyMissing), result);
    }

    [Fact]
    public async Task directory_that_is_not_a_worktree_is_left_untouched_with_a_warning()
    {
        string stranger = _sandbox.WorktreePath("stranger");
        Directory.CreateDirectory(stranger);
        File.WriteAllText(Path.Combine(stranger, "keep.txt"), "keep");

        WorktreeCleanupResult result = await _workspace.CleanupWorktreeAsync(_sandbox.Location, stranger, CancellationToken.None);

        Assert.Equal(WorktreeCleanupOutcome.AlreadyMissing, result.Outcome);
        Assert.NotNull(result.Warning);
        Assert.True(File.Exists(Path.Combine(stranger, "keep.txt")));
    }

    public ValueTask DisposeAsync()
    {
        _sandbox.Dispose();
        return ValueTask.CompletedTask;
    }
}
