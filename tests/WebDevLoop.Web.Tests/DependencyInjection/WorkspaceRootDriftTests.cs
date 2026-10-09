using WebDevLoop.Core.Domain;
using WebDevLoop.Web.DependencyInjection;

namespace WebDevLoop.Web.Tests.DependencyInjection;

public sealed class WorkspaceRootDriftTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "drift-root"));

    private static RepositoryRecord RepositoryAt(string name, string localPath) =>
        RepositoryRecord.Register(new GitHubRepoRef("acme", name), new BranchName("main"), "https://example.invalid/x.git", localPath, DateTimeOffset.UnixEpoch);

    [Fact]
    public void only_clones_outside_the_workspace_root_are_reported()
    {
        RepositoryRecord inside = RepositoryAt("inside", Path.Combine(Root, "repos", "acme", "inside"));
        RepositoryRecord moved = RepositoryAt("moved", Path.Combine(Path.GetTempPath(), "old-root", "repos", "acme", "moved"));
        RepositoryRecord sibling = RepositoryAt("sibling", Root + "-other");
        RepositoryRecord escaping = RepositoryAt("escaping", Path.Combine(Root, "..", "escaped"));

        IReadOnlyList<RepositoryRecord> outside = WorkspaceRootDrift.FindOutside(Root + Path.DirectorySeparatorChar, [inside, moved, sibling, escaping]);

        Assert.Equal([moved, sibling, escaping], outside);
    }
}
