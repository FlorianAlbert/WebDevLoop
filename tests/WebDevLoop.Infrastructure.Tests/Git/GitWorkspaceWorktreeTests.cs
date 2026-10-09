using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Git;

namespace WebDevLoop.Infrastructure.Tests.Git;

public sealed class GitWorkspaceWorktreeTests : IAsyncLifetime
{
    private static readonly RunId Run = new("run-1");
    private static readonly BranchName Ticket1 = RunScopedNaming.TicketBranch(Run, new TicketRunId("t-1"));
    private static readonly BranchName Ticket2 = RunScopedNaming.TicketBranch(Run, new TicketRunId("t-2"));
    private readonly GitSandbox _sandbox = new();
    private readonly GitWorkspace _workspace;
    private CommitSha _second;

    public GitWorkspaceWorktreeTests() => _workspace = _sandbox.CreateWorkspace();

    private string Path1 => _sandbox.WorktreePath("t-1");

    public async ValueTask InitializeAsync()
    {
        _second = _sandbox.CommitToRemote(GitSandbox.Main, new Dictionary<string, string> { ["src/a.txt"] = "a" }, "second");
        await _workspace.EnsureClonedAsync(_sandbox.Location, CancellationToken.None);
    }

    [Fact]
    public async Task prepare_creates_the_ticket_branch_at_the_expected_start_sha_and_checks_it_out()
    {
        TicketWorktree worktree = await _workspace.PrepareWorktreeAsync(_sandbox.Location, new WorktreeSpec(Ticket1, _sandbox.InitialCommit, Path1), CancellationToken.None);

        Assert.Equal(new TicketWorktree(Path1, Ticket1, _sandbox.InitialCommit), worktree);
        Assert.Equal(_sandbox.InitialCommit, await _workspace.GetBranchTipAsync(_sandbox.Location, Ticket1, GitRefScope.Local, CancellationToken.None));
        Assert.True(File.Exists(Path.Combine(Path1, "README.md")));
        Assert.False(File.Exists(Path.Combine(Path1, "src", "a.txt")));
        Assert.Equal(
            new WorktreeInspection(WorktreeStatus.Clean, Ticket1, _sandbox.InitialCommit),
            await _workspace.InspectWorktreeAsync(_sandbox.Location, Path1, CancellationToken.None));
    }

    [Fact]
    public async Task prepare_leaves_no_helper_branches_behind()
    {
        await _workspace.PrepareWorktreeAsync(_sandbox.Location, new WorktreeSpec(Ticket1, _second, Path1), CancellationToken.None);

        Assert.Equal(["main", Ticket1.Value], GitSandbox.LocalBranches(_sandbox.Location.LocalPath));
    }

    [Fact]
    public async Task implementation_report_sha_is_verifiable_against_the_worktree_and_ticket_branch_ancestry()
    {
        await _workspace.PrepareWorktreeAsync(_sandbox.Location, new WorktreeSpec(Ticket1, _second, Path1), CancellationToken.None);
        CommitSha reported = GitSandbox.CommitInWorktree(Path1, new Dictionary<string, string> { ["src/b.txt"] = "b" }, "implement");

        WorktreeInspection inspection = await _workspace.InspectWorktreeAsync(_sandbox.Location, Path1, CancellationToken.None);

        Assert.Equal(new WorktreeInspection(WorktreeStatus.Clean, Ticket1, reported), inspection);
        Assert.Equal(reported, await _workspace.GetBranchTipAsync(_sandbox.Location, Ticket1, GitRefScope.Local, CancellationToken.None));
        Assert.True(await _workspace.IsAncestorAsync(_sandbox.Location, _second, reported, CancellationToken.None));
    }

    [Fact]
    public async Task preparing_an_existing_worktree_resets_it_to_the_new_start_point()
    {
        await _workspace.PrepareWorktreeAsync(_sandbox.Location, new WorktreeSpec(Ticket1, _sandbox.InitialCommit, Path1), CancellationToken.None);
        GitSandbox.CommitInWorktree(Path1, new Dictionary<string, string> { ["abandoned.txt"] = "x" }, "abandoned attempt");
        File.WriteAllText(Path.Combine(Path1, "scratch.txt"), "untracked");

        TicketWorktree reset = await _workspace.PrepareWorktreeAsync(_sandbox.Location, new WorktreeSpec(Ticket1, _second, Path1), CancellationToken.None);

        Assert.Equal(_second, reset.Head);
        Assert.Equal(
            new WorktreeInspection(WorktreeStatus.Clean, Ticket1, _second),
            await _workspace.InspectWorktreeAsync(_sandbox.Location, Path1, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(Path1, "abandoned.txt")));
        Assert.False(File.Exists(Path.Combine(Path1, "scratch.txt")));
    }

    [Fact]
    public async Task preparing_a_path_that_holds_another_branch_is_refused()
    {
        await _workspace.PrepareWorktreeAsync(_sandbox.Location, new WorktreeSpec(Ticket1, _second, Path1), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _workspace.PrepareWorktreeAsync(_sandbox.Location, new WorktreeSpec(Ticket2, _second, Path1), CancellationToken.None));
    }

    [Fact]
    public async Task preparing_from_an_unknown_start_sha_is_refused()
    {
        var unknown = new CommitSha(new string('c', 40));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _workspace.PrepareWorktreeAsync(_sandbox.Location, new WorktreeSpec(Ticket1, unknown, Path1), CancellationToken.None));
        Assert.False(Directory.Exists(Path1));
    }

    [Fact]
    public async Task parallel_tickets_get_independent_worktrees()
    {
        TicketWorktree[] worktrees = await Task.WhenAll(
            _workspace.PrepareWorktreeAsync(_sandbox.Location, new WorktreeSpec(Ticket1, _second, Path1), CancellationToken.None),
            _workspace.PrepareWorktreeAsync(_sandbox.Location, new WorktreeSpec(Ticket2, _second, _sandbox.WorktreePath("t-2")), CancellationToken.None));

        foreach (TicketWorktree worktree in worktrees)
        {
            WorktreeInspection inspection = await _workspace.InspectWorktreeAsync(_sandbox.Location, worktree.Path, CancellationToken.None);
            Assert.Equal(new WorktreeInspection(WorktreeStatus.Clean, worktree.Branch, _second), inspection);
        }
    }

    [Fact]
    public async Task uncommitted_or_untracked_changes_make_the_worktree_dirty()
    {
        await _workspace.PrepareWorktreeAsync(_sandbox.Location, new WorktreeSpec(Ticket1, _second, Path1), CancellationToken.None);
        File.WriteAllText(Path.Combine(Path1, "new.txt"), "untracked");

        WorktreeInspection inspection = await _workspace.InspectWorktreeAsync(_sandbox.Location, Path1, CancellationToken.None);

        Assert.Equal(WorktreeStatus.Dirty, inspection.Status);
    }

    [Fact]
    public async Task locked_worktree_is_reported_as_locked()
    {
        await _workspace.PrepareWorktreeAsync(_sandbox.Location, new WorktreeSpec(Ticket1, _second, Path1), CancellationToken.None);
        GitSandbox.LockWorktree(_sandbox.Location.LocalPath, Path1);

        WorktreeInspection inspection = await _workspace.InspectWorktreeAsync(_sandbox.Location, Path1, CancellationToken.None);

        Assert.Equal(new WorktreeInspection(WorktreeStatus.Locked, Ticket1, _second), inspection);
    }

    [Fact]
    public async Task unknown_worktree_path_is_reported_as_missing()
    {
        WorktreeInspection inspection = await _workspace.InspectWorktreeAsync(_sandbox.Location, Path1, CancellationToken.None);

        Assert.Equal(new WorktreeInspection(WorktreeStatus.Missing, null, null), inspection);
    }

    public ValueTask DisposeAsync()
    {
        _sandbox.Dispose();
        return ValueTask.CompletedTask;
    }
}
