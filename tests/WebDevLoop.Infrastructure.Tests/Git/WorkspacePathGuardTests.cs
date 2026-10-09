using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Git;

namespace WebDevLoop.Infrastructure.Tests.Git;

public sealed class WorkspacePathGuardTests : IDisposable
{
    private readonly GitSandbox _sandbox = new();

    [Theory]
    [InlineData("worktrees/ticket-1")]
    [InlineData("clones/acme/../acme/widgets")]
    public void paths_under_the_root_are_normalized_and_accepted(string relative)
    {
        var guard = new WorkspacePathGuard(_sandbox.WorkspaceRoot);

        string confined = guard.Confine(Path.Combine(_sandbox.WorkspaceRoot, relative));

        Assert.Equal(Path.GetFullPath(Path.Combine(_sandbox.WorkspaceRoot, relative)), confined);
    }

    [Theory]
    [InlineData("../elsewhere")]
    [InlineData("worktrees/../../elsewhere")]
    [InlineData("")]
    [InlineData("../workspace-sibling")]
    public void paths_outside_or_equal_to_the_root_are_rejected(string relative)
    {
        var guard = new WorkspacePathGuard(_sandbox.WorkspaceRoot);

        Assert.Throws<WorkspacePathOutsideRootException>(() => guard.Confine(Path.Combine(_sandbox.WorkspaceRoot, relative)));
    }

    [Fact]
    public void sibling_directory_sharing_the_root_prefix_is_rejected()
    {
        var guard = new WorkspacePathGuard(_sandbox.WorkspaceRoot);

        Assert.Throws<WorkspacePathOutsideRootException>(() => guard.Confine(_sandbox.WorkspaceRoot + "-evil" + Path.DirectorySeparatorChar + "x"));
    }

    [Fact]
    public async Task worktree_path_outside_root_is_rejected_before_any_git_change()
    {
        GitWorkspace workspace = _sandbox.CreateWorkspace();
        await workspace.EnsureClonedAsync(_sandbox.Location, CancellationToken.None);
        string outside = Path.Combine(_sandbox.Root, "outside-worktree");
        var spec = new WorktreeSpec(new("webdevloop/run-1/ticket/t-1"), _sandbox.InitialCommit, outside);

        await Assert.ThrowsAsync<WorkspacePathOutsideRootException>(() => workspace.PrepareWorktreeAsync(_sandbox.Location, spec, CancellationToken.None));

        Assert.False(Directory.Exists(outside));
        Assert.Null(await workspace.GetBranchTipAsync(_sandbox.Location, spec.Branch, GitRefScope.Local, CancellationToken.None));
    }

    [Fact]
    public async Task clone_path_outside_root_is_rejected()
    {
        GitWorkspace workspace = _sandbox.CreateWorkspace();
        GitRepositoryLocation outside = _sandbox.Location with { LocalPath = Path.Combine(_sandbox.Root, "outside-clone") };

        await Assert.ThrowsAsync<WorkspacePathOutsideRootException>(() => workspace.EnsureClonedAsync(outside, CancellationToken.None));

        Assert.False(Directory.Exists(outside.LocalPath));
    }

    [Fact]
    public async Task worktree_operations_reject_paths_outside_root()
    {
        GitWorkspace workspace = _sandbox.CreateWorkspace();
        string outside = Path.Combine(_sandbox.Root, "outside-worktree");
        var worktree = new TicketWorktree(outside, new("webdevloop/run-1/ticket/t-1"), _sandbox.InitialCommit);

        await Assert.ThrowsAsync<WorkspacePathOutsideRootException>(() => workspace.InspectWorktreeAsync(_sandbox.Location, outside, CancellationToken.None));
        await Assert.ThrowsAsync<WorkspacePathOutsideRootException>(() => workspace.CleanupWorktreeAsync(_sandbox.Location, outside, CancellationToken.None));
        await Assert.ThrowsAsync<WorkspacePathOutsideRootException>(() => workspace.MergeIntoWorktreeAsync(worktree, _sandbox.InitialCommit, "merge", CancellationToken.None));
    }

    public void Dispose() => _sandbox.Dispose();
}
