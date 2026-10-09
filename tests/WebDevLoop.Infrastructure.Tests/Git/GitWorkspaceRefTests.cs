using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Git;

namespace WebDevLoop.Infrastructure.Tests.Git;

public sealed class GitWorkspaceRefTests : IAsyncLifetime
{
    private static readonly BranchName Integration = RunScopedNaming.IntegrationBranch(new RunId("run-1"));
    private readonly GitSandbox _sandbox = new();
    private readonly GitWorkspace _workspace;
    private CommitSha _second;

    public GitWorkspaceRefTests() => _workspace = _sandbox.CreateWorkspace();

    public async ValueTask InitializeAsync()
    {
        _second = _sandbox.CommitToRemote(GitSandbox.Main, new Dictionary<string, string> { ["src/a.txt"] = "a", ["README.md"] = "changed\n" }, "second");
        await _workspace.EnsureClonedAsync(_sandbox.Location, CancellationToken.None);
    }

    [Fact]
    public async Task run_scoped_integration_branch_is_created_from_the_expected_base_sha()
    {
        RefUpdateResult result = await _workspace.UpdateBranchAsync(_sandbox.Location, Integration, _sandbox.InitialCommit, expectedPriorTip: null, CancellationToken.None);

        Assert.Equal(new RefUpdateResult(RefUpdateOutcome.Updated, _sandbox.InitialCommit), result);
        Assert.Equal(_sandbox.InitialCommit, await _workspace.GetBranchTipAsync(_sandbox.Location, Integration, GitRefScope.Local, CancellationToken.None));
    }

    [Fact]
    public async Task creating_a_branch_that_already_exists_elsewhere_is_a_mismatch()
    {
        await _workspace.UpdateBranchAsync(_sandbox.Location, Integration, _sandbox.InitialCommit, null, CancellationToken.None);

        RefUpdateResult result = await _workspace.UpdateBranchAsync(_sandbox.Location, Integration, _second, expectedPriorTip: null, CancellationToken.None);

        Assert.Equal(new RefUpdateResult(RefUpdateOutcome.ExpectedPriorMismatch, _sandbox.InitialCommit), result);
    }

    [Fact]
    public async Task branch_moves_when_the_expected_prior_tip_matches()
    {
        await _workspace.UpdateBranchAsync(_sandbox.Location, Integration, _sandbox.InitialCommit, null, CancellationToken.None);

        RefUpdateResult result = await _workspace.UpdateBranchAsync(_sandbox.Location, Integration, _second, _sandbox.InitialCommit, CancellationToken.None);

        Assert.Equal(new RefUpdateResult(RefUpdateOutcome.Updated, _second), result);
        Assert.Equal(_second, await _workspace.GetBranchTipAsync(_sandbox.Location, Integration, GitRefScope.Local, CancellationToken.None));
    }

    [Fact]
    public async Task expected_prior_sha_mismatch_prevents_the_integration_ref_update()
    {
        await _workspace.UpdateBranchAsync(_sandbox.Location, Integration, _second, null, CancellationToken.None);
        CommitSha stale = _sandbox.InitialCommit;
        CommitSha other = _sandbox.CommitToRemote(new("other"), new Dictionary<string, string> { ["o.txt"] = "o" }, "other");
        await _workspace.FetchAsync(_sandbox.Location, CancellationToken.None);

        RefUpdateResult result = await _workspace.UpdateBranchAsync(_sandbox.Location, Integration, other, expectedPriorTip: stale, CancellationToken.None);

        Assert.Equal(new RefUpdateResult(RefUpdateOutcome.ExpectedPriorMismatch, _second), result);
        Assert.False(result.Succeeded);
        Assert.Equal(_second, await _workspace.GetBranchTipAsync(_sandbox.Location, Integration, GitRefScope.Local, CancellationToken.None));
    }

    [Fact]
    public async Task replaying_an_update_that_already_happened_is_reported_as_already_at_target()
    {
        await _workspace.UpdateBranchAsync(_sandbox.Location, Integration, _sandbox.InitialCommit, null, CancellationToken.None);
        await _workspace.UpdateBranchAsync(_sandbox.Location, Integration, _second, _sandbox.InitialCommit, CancellationToken.None);

        RefUpdateResult replay = await _workspace.UpdateBranchAsync(_sandbox.Location, Integration, _second, _sandbox.InitialCommit, CancellationToken.None);

        Assert.Equal(new RefUpdateResult(RefUpdateOutcome.AlreadyAtTarget, _second), replay);
    }

    [Fact]
    public async Task updating_to_an_unknown_commit_is_rejected()
    {
        var unknown = new CommitSha(new string('a', 40));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _workspace.UpdateBranchAsync(_sandbox.Location, Integration, unknown, null, CancellationToken.None));
    }

    [Fact]
    public async Task concurrent_compare_and_swap_updates_let_exactly_one_win()
    {
        await _workspace.UpdateBranchAsync(_sandbox.Location, Integration, _sandbox.InitialCommit, null, CancellationToken.None);
        CommitSha other = _sandbox.CommitToRemote(new("other"), new Dictionary<string, string> { ["o.txt"] = "o" }, "other");
        await _workspace.FetchAsync(_sandbox.Location, CancellationToken.None);

        RefUpdateResult[] results = await Task.WhenAll(
            _workspace.UpdateBranchAsync(_sandbox.Location, Integration, _second, _sandbox.InitialCommit, CancellationToken.None),
            _workspace.UpdateBranchAsync(_sandbox.Location, Integration, other, _sandbox.InitialCommit, CancellationToken.None));

        Assert.Single(results, r => r.Outcome == RefUpdateOutcome.Updated);
        Assert.Single(results, r => r.Outcome == RefUpdateOutcome.ExpectedPriorMismatch);
    }

    [Fact]
    public async Task ancestry_is_reported_for_reachable_commits_only()
    {
        Assert.True(await _workspace.IsAncestorAsync(_sandbox.Location, _sandbox.InitialCommit, _second, CancellationToken.None));
        Assert.True(await _workspace.IsAncestorAsync(_sandbox.Location, _second, _second, CancellationToken.None));
        Assert.False(await _workspace.IsAncestorAsync(_sandbox.Location, _second, _sandbox.InitialCommit, CancellationToken.None));
        Assert.False(await _workspace.IsAncestorAsync(_sandbox.Location, new CommitSha(new string('b', 40)), _second, CancellationToken.None));
    }

    [Fact]
    public async Task changed_files_lists_paths_between_two_commits()
    {
        IReadOnlyList<string> changed = await _workspace.GetChangedFilesAsync(_sandbox.Location, _sandbox.InitialCommit, _second, CancellationToken.None);

        Assert.Equal(["README.md", "src/a.txt"], changed);
    }

    public ValueTask DisposeAsync()
    {
        _sandbox.Dispose();
        return ValueTask.CompletedTask;
    }
}
