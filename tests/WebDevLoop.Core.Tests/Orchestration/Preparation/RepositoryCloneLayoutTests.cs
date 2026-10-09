using WebDevLoop.Core.Domain;

using WebDevLoop.Core.Orchestration.Preparation;

namespace WebDevLoop.Core.Tests.Orchestration.Preparation;

public sealed class RepositoryCloneLayoutTests
{
    private static readonly string Root = Path.GetFullPath("/srv/workspaces");

    [Fact]
    public void the_clone_lives_under_repos_owner_name_apart_from_the_run_directories()
    {
        string path = RepositoryCloneLayout.PathFor(Root, new GitHubRepoRef("acme", "widgets"));

        Assert.Equal(Path.Combine(Root, "repos", "acme", "widgets"), path);
        Assert.DoesNotContain("runs", Path.GetRelativePath(Root, path).Split(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void a_relative_workspace_root_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => RepositoryCloneLayout.PathFor("relative/root", new GitHubRepoRef("acme", "widgets")));
    }

    [Theory]
    [InlineData("acme", true)]
    [InlineData("my-repo_1.0", true)]
    [InlineData(".github", true)]
    [InlineData("", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    public void only_plain_github_names_are_safe_path_segments(string segment, bool expected)
    {
        Assert.Equal(expected, RepositoryCloneLayout.IsSafeSegment(segment));
    }

    [Fact]
    public void an_unsafe_owner_or_name_is_rejected_when_building_the_path()
    {
        Assert.Throws<ArgumentException>(() => RepositoryCloneLayout.PathFor(Root, new GitHubRepoRef("..", "widgets")));
        Assert.Throws<ArgumentException>(() => RepositoryCloneLayout.PathFor(Root, new GitHubRepoRef("acme", "a/b")));
    }
}
