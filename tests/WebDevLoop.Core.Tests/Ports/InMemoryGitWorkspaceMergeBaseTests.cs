using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Ports.Fakes;

namespace WebDevLoop.Core.Tests.Ports;

public sealed class InMemoryGitWorkspaceMergeBaseTests
{
    private static readonly GitRepositoryLocation Location = new(new GitHubRepoRef("acme", "widgets"), "origin", "clone");

    private readonly InMemoryGitWorkspace _git = new();

    [Fact]
    public async Task Merge_base_of_a_merged_branch_and_a_moved_on_tip_is_the_commit_that_was_merged()
    {
        CommitSha root = _git.Commit([], "a");
        CommitSha merged = _git.Commit([root], "b");
        CommitSha ticket = _git.Commit([_git.Commit([root], "t"), merged]);
        CommitSha movedTip = _git.Commit([merged], "c");

        Assert.Equal(merged, await _git.MergeBaseAsync(Location, ticket, movedTip, CancellationToken.None));
        Assert.Equal(merged, await _git.MergeBaseAsync(Location, movedTip, ticket, CancellationToken.None));
    }

    [Fact]
    public async Task Merge_base_of_an_ancestor_is_the_ancestor_and_unrelated_histories_have_none()
    {
        CommitSha root = _git.Commit([], "a");
        CommitSha child = _git.Commit([root], "b");
        CommitSha unrelated = _git.Commit([], "x");

        Assert.Equal(root, await _git.MergeBaseAsync(Location, child, root, CancellationToken.None));
        Assert.Null(await _git.MergeBaseAsync(Location, child, unrelated, CancellationToken.None));
        Assert.Null(await _git.MergeBaseAsync(Location, child, new CommitSha(new string('f', 40)), CancellationToken.None));
    }
}
