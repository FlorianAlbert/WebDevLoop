using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Git;

namespace WebDevLoop.Infrastructure.Tests.Git;

public sealed class GitWorkspaceMergeTests : IAsyncLifetime
{
    private static readonly RunId Run = new("run-1");
    private static readonly BranchName Integration = RunScopedNaming.IntegrationBranch(Run);
    private static readonly BranchName Ticket1 = RunScopedNaming.TicketBranch(Run, new TicketRunId("t-1"));
    private static readonly BranchName Ticket2 = RunScopedNaming.TicketBranch(Run, new TicketRunId("t-2"));
    private readonly GitSandbox _sandbox = new();
    private readonly GitWorkspace _workspace;

    public GitWorkspaceMergeTests() => _workspace = _sandbox.CreateWorkspace();

    private string Clone => _sandbox.Location.LocalPath;

    public async ValueTask InitializeAsync()
    {
        await _workspace.EnsureClonedAsync(_sandbox.Location, CancellationToken.None);
        await _workspace.UpdateBranchAsync(_sandbox.Location, Integration, _sandbox.InitialCommit, null, CancellationToken.None);
    }

    [Fact]
    public async Task squash_merge_descriptor_has_exactly_one_parent_the_prior_integration_tip()
    {
        TicketWorktree ticket = await PrepareAsync(Ticket1, "t-1", _sandbox.InitialCommit);
        GitSandbox.CommitInWorktree(ticket.Path, new Dictionary<string, string> { ["src/one.txt"] = "1" }, "step 1");
        CommitSha ticketTip = GitSandbox.CommitInWorktree(ticket.Path, new Dictionary<string, string> { ["src/two.txt"] = "2" }, "step 2");

        GitMergeResult result = await _workspace.CreateSquashCommitAsync(_sandbox.Location, new SquashRequest(ticketTip, _sandbox.InitialCommit, "Integrate t-1"), CancellationToken.None);

        Assert.Equal(GitMergeOutcome.Merged, result.Outcome);
        CommitSha squash = result.Commit!.Value;
        Assert.Equal([_sandbox.InitialCommit.Value], GitSandbox.ParentsOf(Clone, squash));
        Assert.Equal("1", GitSandbox.ReadFileAt(Clone, squash, "src/one.txt"));
        Assert.Equal("2", GitSandbox.ReadFileAt(Clone, squash, "src/two.txt"));
        Assert.Equal("Integrate t-1", GitSandbox.MessageOf(Clone, squash));
        Assert.Equal(_sandbox.InitialCommit, await _workspace.GetBranchTipAsync(_sandbox.Location, Integration, GitRefScope.Local, CancellationToken.None));
    }

    [Fact]
    public async Task squash_onto_a_moved_integration_tip_keeps_both_changes()
    {
        TicketWorktree first = await PrepareAsync(Ticket1, "t-1", _sandbox.InitialCommit);
        TicketWorktree second = await PrepareAsync(Ticket2, "t-2", _sandbox.InitialCommit);
        CommitSha firstTip = GitSandbox.CommitInWorktree(first.Path, new Dictionary<string, string> { ["one.txt"] = "1" }, "one");
        CommitSha secondTip = GitSandbox.CommitInWorktree(second.Path, new Dictionary<string, string> { ["two.txt"] = "2" }, "two");
        CommitSha integrated = (await _workspace.CreateSquashCommitAsync(_sandbox.Location, new SquashRequest(firstTip, _sandbox.InitialCommit, "one"), CancellationToken.None)).Commit!.Value;

        GitMergeResult result = await _workspace.CreateSquashCommitAsync(_sandbox.Location, new SquashRequest(secondTip, integrated, "two"), CancellationToken.None);

        Assert.Equal([integrated.Value], GitSandbox.ParentsOf(Clone, result.Commit!.Value));
        Assert.Equal("1", GitSandbox.ReadFileAt(Clone, result.Commit.Value, "one.txt"));
        Assert.Equal("2", GitSandbox.ReadFileAt(Clone, result.Commit.Value, "two.txt"));
    }

    [Fact]
    public async Task conflicting_squash_reports_conflicted_paths_and_creates_nothing()
    {
        TicketWorktree first = await PrepareAsync(Ticket1, "t-1", _sandbox.InitialCommit);
        TicketWorktree second = await PrepareAsync(Ticket2, "t-2", _sandbox.InitialCommit);
        CommitSha firstTip = GitSandbox.CommitInWorktree(first.Path, new Dictionary<string, string> { ["README.md"] = "first\n" }, "first");
        CommitSha secondTip = GitSandbox.CommitInWorktree(second.Path, new Dictionary<string, string> { ["README.md"] = "second\n" }, "second");
        CommitSha integrated = (await _workspace.CreateSquashCommitAsync(_sandbox.Location, new SquashRequest(firstTip, _sandbox.InitialCommit, "first"), CancellationToken.None)).Commit!.Value;

        GitMergeResult result = await _workspace.CreateSquashCommitAsync(_sandbox.Location, new SquashRequest(secondTip, integrated, "second"), CancellationToken.None);

        Assert.Equal(GitMergeOutcome.Conflicted, result.Outcome);
        Assert.Null(result.Commit);
        Assert.Equal(["README.md"], result.ConflictedPaths);
    }

    [Fact]
    public async Task squash_of_a_source_already_contained_in_the_integration_tip_is_already_up_to_date()
    {
        TicketWorktree ticket = await PrepareAsync(Ticket1, "t-1", _sandbox.InitialCommit);
        CommitSha ticketTip = GitSandbox.CommitInWorktree(ticket.Path, new Dictionary<string, string> { ["one.txt"] = "1" }, "one");
        CommitSha integrated = (await _workspace.CreateSquashCommitAsync(_sandbox.Location, new SquashRequest(ticketTip, _sandbox.InitialCommit, "one"), CancellationToken.None)).Commit!.Value;

        GitMergeResult replay = await _workspace.CreateSquashCommitAsync(_sandbox.Location, new SquashRequest(ticketTip, integrated, "one"), CancellationToken.None);

        Assert.Equal(GitMergeResult.AlreadyUpToDate(integrated), replay);
    }

    [Fact]
    public async Task merging_the_integration_tip_into_a_diverged_ticket_creates_a_merge_commit()
    {
        TicketWorktree other = await PrepareAsync(Ticket2, "t-2", _sandbox.InitialCommit);
        CommitSha otherTip = GitSandbox.CommitInWorktree(other.Path, new Dictionary<string, string> { ["other.txt"] = "o" }, "other");
        CommitSha integrated = (await _workspace.CreateSquashCommitAsync(_sandbox.Location, new SquashRequest(otherTip, _sandbox.InitialCommit, "other"), CancellationToken.None)).Commit!.Value;
        TicketWorktree ticket = await PrepareAsync(Ticket1, "t-1", _sandbox.InitialCommit);
        CommitSha ticketTip = GitSandbox.CommitInWorktree(ticket.Path, new Dictionary<string, string> { ["mine.txt"] = "m" }, "mine");

        GitMergeResult result = await _workspace.MergeIntoWorktreeAsync(ticket with { Head = ticketTip }, integrated, "Merge integration", CancellationToken.None);

        Assert.Equal(GitMergeOutcome.Merged, result.Outcome);
        Assert.Equal([ticketTip.Value, integrated.Value], GitSandbox.ParentsOf(Clone, result.Commit!.Value));
        Assert.Equal("Merge integration", GitSandbox.MessageOf(Clone, result.Commit.Value));
        Assert.Equal(
            new WorktreeInspection(WorktreeStatus.Clean, Ticket1, result.Commit.Value),
            await _workspace.InspectWorktreeAsync(_sandbox.Location, ticket.Path, CancellationToken.None));
    }

    [Fact]
    public async Task merging_an_already_contained_source_is_already_up_to_date()
    {
        TicketWorktree ticket = await PrepareAsync(Ticket1, "t-1", _sandbox.InitialCommit);
        CommitSha ticketTip = GitSandbox.CommitInWorktree(ticket.Path, new Dictionary<string, string> { ["mine.txt"] = "m" }, "mine");

        GitMergeResult result = await _workspace.MergeIntoWorktreeAsync(ticket with { Head = ticketTip }, _sandbox.InitialCommit, "noop", CancellationToken.None);

        Assert.Equal(GitMergeResult.AlreadyUpToDate(ticketTip), result);
    }

    [Fact]
    public async Task conflicting_merge_into_worktree_reports_paths_and_leaves_conflicts_for_the_resolver()
    {
        TicketWorktree other = await PrepareAsync(Ticket2, "t-2", _sandbox.InitialCommit);
        CommitSha otherTip = GitSandbox.CommitInWorktree(other.Path, new Dictionary<string, string> { ["README.md"] = "theirs\n" }, "theirs");
        CommitSha integrated = (await _workspace.CreateSquashCommitAsync(_sandbox.Location, new SquashRequest(otherTip, _sandbox.InitialCommit, "theirs"), CancellationToken.None)).Commit!.Value;
        TicketWorktree ticket = await PrepareAsync(Ticket1, "t-1", _sandbox.InitialCommit);
        CommitSha ticketTip = GitSandbox.CommitInWorktree(ticket.Path, new Dictionary<string, string> { ["README.md"] = "mine\n" }, "mine");

        GitMergeResult result = await _workspace.MergeIntoWorktreeAsync(ticket with { Head = ticketTip }, integrated, "Merge integration", CancellationToken.None);

        Assert.Equal(GitMergeResult.Conflicted(["README.md"]).Outcome, result.Outcome);
        Assert.Equal(["README.md"], result.ConflictedPaths);
        Assert.Contains("<<<<<<<", File.ReadAllText(Path.Combine(ticket.Path, "README.md")));
        Assert.Equal(WorktreeStatus.Dirty, (await _workspace.InspectWorktreeAsync(_sandbox.Location, ticket.Path, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task merging_into_a_worktree_on_another_branch_is_refused()
    {
        TicketWorktree ticket = await PrepareAsync(Ticket1, "t-1", _sandbox.InitialCommit);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _workspace.MergeIntoWorktreeAsync(ticket with { Branch = Ticket2 }, _sandbox.InitialCommit, "merge", CancellationToken.None));
    }

    private Task<TicketWorktree> PrepareAsync(BranchName branch, string directory, CommitSha start) =>
        _workspace.PrepareWorktreeAsync(_sandbox.Location, new WorktreeSpec(branch, start, _sandbox.WorktreePath(directory)), CancellationToken.None);

    public ValueTask DisposeAsync()
    {
        _sandbox.Dispose();
        return ValueTask.CompletedTask;
    }
}
