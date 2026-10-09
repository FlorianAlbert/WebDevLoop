using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Tests.Ports;

public sealed class ImplementerPolicyTests
{
    private const string Worktree = "/work/trees/t1";
    private static readonly RoleCapabilityPolicy Policy =
        RoleCapabilityPolicies.For(AgentRole.Implementer, new AgentWorkspace(Worktree, NotesDirectory: "/work/notes/run1"));

    [Theory]
    [InlineData(AgentCapability.ReadFiles)]
    [InlineData(AgentCapability.WriteFiles)]
    [InlineData(AgentCapability.RunShellCommands)]
    [InlineData(AgentCapability.CreateLocalCommit)]
    [InlineData(AgentCapability.ReportResult)]
    public void implementer_may_edit_test_commit_locally_and_report(AgentCapability capability)
    {
        Assert.True(Policy.Allows(capability));
    }

    [Theory]
    [InlineData(AgentCapability.PushRefs)]
    [InlineData(AgentCapability.FetchRemote)]
    [InlineData(AgentCapability.CreatePullRequest)]
    [InlineData(AgentCapability.ManageIssues)]
    [InlineData(AgentCapability.ManageStacks)]
    [InlineData(AgentCapability.ManageWorktrees)]
    [InlineData(AgentCapability.ManageBranches)]
    [InlineData(AgentCapability.WriteNotes)]
    [InlineData(AgentCapability.UseBrowser)]
    public void implementer_may_not_publish_or_manage_github_branches_or_worktrees(AgentCapability capability)
    {
        Assert.False(Policy.Allows(capability));
    }

    [Fact]
    public void implementer_reports_through_report_implementation_without_a_github_write_token()
    {
        Assert.Equal(AgentRole.Implementer, Policy.Role);
        Assert.Equal(AgentReportTools.Implementation, Policy.ReportToolName);
        Assert.Equal(GitHubTokenAccess.ReadOnlyIfRequired, Policy.TokenAccess);
        Assert.False(Policy.RequiresGitHubWriteToken);
    }

    [Theory]
    [InlineData("git push origin HEAD")]
    [InlineData("git -C /work/trees/t1 push")]
    [InlineData("git --git-dir=/work/repo/.git push")]
    [InlineData("dotnet test && git push")]
    [InlineData("dotnet build; git push --force")]
    [InlineData("GIT_TRACE=1 git push")]
    [InlineData("sudo git push")]
    [InlineData("env FOO=1 git push")]
    [InlineData("bash -c 'git push --force'")]
    [InlineData("sh -lc \"cd x && git push\"")]
    [InlineData("echo $(git push)")]
    [InlineData("echo `gh pr list`")]
    [InlineData("/usr/bin/git fetch origin")]
    [InlineData("git pull --rebase")]
    [InlineData("git remote add evil https://example.com/x.git")]
    [InlineData("git worktree add ../other")]
    [InlineData("git clone https://github.com/octo/app.git")]
    [InlineData("git branch -D main")]
    [InlineData("git checkout -b other")]
    [InlineData("git switch -c other")]
    [InlineData("gh pr create --fill")]
    [InlineData("gh issue close 3")]
    [InlineData("gh stack submit")]
    [InlineData("gh api repos/octo/app/issues -f title=x")]
    [InlineData("git credential fill")]
    public void implementer_shell_denies_push_fetch_branch_worktree_and_github_commands(string commandLine)
    {
        Assert.False(Policy.IsCommandAllowed(commandLine), commandLine);
        Assert.NotNull(Policy.FindDeniedCommand(commandLine));
    }

    [Theory]
    [InlineData("git status")]
    [InlineData("git diff HEAD~1")]
    [InlineData("git log --oneline -5")]
    [InlineData("git add -A && git commit -m 'feat: push button'")]
    [InlineData("git commit -m \"gh pr create is mentioned here\"")]
    [InlineData("git checkout -- src/a.cs")]
    [InlineData("git merge --continue")]
    [InlineData("dotnet test")]
    [InlineData("echo 'git push'")]
    public void implementer_shell_allows_local_build_test_and_commit_commands(string commandLine)
    {
        Assert.True(Policy.IsCommandAllowed(commandLine), commandLine);
    }

    [Theory]
    [InlineData("/work/trees/t1/src/a.cs", true)]
    [InlineData("src/a.cs", true)]
    [InlineData("/work/trees/t1", true)]
    [InlineData("/work/trees/t1/../t2/a.cs", false)]
    [InlineData("/work/trees/t10/a.cs", false)]
    [InlineData("/work/notes/run1/notes.md", false)]
    [InlineData("/etc/passwd", false)]
    public void implementer_writes_are_confined_to_the_assigned_worktree(string path, bool allowed)
    {
        Assert.Equal(allowed, Policy.Paths.CanWrite(path));
    }

    [Theory]
    [InlineData("/work/notes/run1/notes.md", true)]
    [InlineData("/work/trees/t1/README.md", true)]
    [InlineData("/home/user/.ssh/id_rsa", false)]
    public void implementer_reads_its_worktree_and_shared_exploration_notes_only(string path, bool allowed)
    {
        Assert.Equal(allowed, Policy.Paths.CanRead(path));
    }

    [Fact]
    public void working_directory_is_the_assigned_worktree()
    {
        Assert.Equal(Worktree, Policy.Paths.WorkingDirectory);
    }
}
