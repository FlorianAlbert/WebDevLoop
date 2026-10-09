using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Tests.Ports;

public sealed class RolePolicyTests
{
    private const string Worktree = "/work/trees/t1";
    private const string Notes = "/work/notes/run1";
    private static readonly AgentWorkspace Workspace = new(Worktree, Notes);

    public static TheoryData<AgentRole> AllRoles => [.. Enum.GetValues<AgentRole>()];

    public static TheoryData<AgentRole> ReadOnlyRoles =>
        [AgentRole.ReviewerCodingStandards, AgentRole.ReviewerSpecification, AgentRole.Tester, AgentRole.Explorer];

    [Theory]
    [MemberData(nameof(AllRoles))]
    public void no_role_requires_a_github_write_token(AgentRole role)
    {
        RoleCapabilityPolicy policy = RoleCapabilityPolicies.For(role, Workspace);

        Assert.False(policy.RequiresGitHubWriteToken);
        Assert.NotEqual(GitHubTokenAccess.Write, policy.TokenAccess);
    }

    [Theory]
    [InlineData(AgentRole.ReviewerCodingStandards)]
    [InlineData(AgentRole.ReviewerSpecification)]
    [InlineData(AgentRole.Tester)]
    [InlineData(AgentRole.ConflictResolver)]
    public void reviewers_tester_and_conflict_resolver_get_no_github_token_at_all(AgentRole role)
    {
        Assert.Equal(GitHubTokenAccess.None, RoleCapabilityPolicies.For(role, Workspace).TokenAccess);
    }

    [Theory]
    [MemberData(nameof(AllRoles))]
    public void every_role_denies_publishing_and_github_cli(AgentRole role)
    {
        RoleCapabilityPolicy policy = RoleCapabilityPolicies.For(role, Workspace);

        Assert.All(
            [AgentCapability.PushRefs, AgentCapability.FetchRemote, AgentCapability.CreatePullRequest, AgentCapability.ManageIssues, AgentCapability.ManageStacks, AgentCapability.ManageWorktrees, AgentCapability.ManageBranches],
            capability => Assert.False(policy.Allows(capability), $"{role} must not have {capability}."));
        Assert.All(
            ["git push", "gh pr create", "gh issue create", "gh stack push", "git worktree add x"],
            command => Assert.False(policy.IsCommandAllowed(command), $"{role} must not run '{command}'."));
        Assert.True(policy.Allows(AgentCapability.ReportResult));
    }

    [Theory]
    [MemberData(nameof(ReadOnlyRoles))]
    public void read_only_roles_cannot_mutate_the_repository(AgentRole role)
    {
        RoleCapabilityPolicy policy = RoleCapabilityPolicies.For(role, Workspace);

        Assert.False(policy.Allows(AgentCapability.WriteFiles));
        Assert.False(policy.Allows(AgentCapability.CreateLocalCommit));
        Assert.False(policy.Paths.CanWrite($"{Worktree}/src/a.cs"));
        Assert.All(
            ["git commit -m x", "git add .", "git reset --hard HEAD~1", "git checkout main", "git rebase main", "git stash", "git merge other"],
            command => Assert.False(policy.IsCommandAllowed(command), $"{role} must not run '{command}'."));
        Assert.All(
            ["git diff main...HEAD", "git log --oneline", "git show HEAD", "dotnet test"],
            command => Assert.True(policy.IsCommandAllowed(command), $"{role} should run '{command}'."));
    }

    [Theory]
    [InlineData(AgentRole.ReviewerCodingStandards)]
    [InlineData(AgentRole.ReviewerSpecification)]
    public void reviewers_read_the_worktree_and_report_reviews(AgentRole role)
    {
        RoleCapabilityPolicy policy = RoleCapabilityPolicies.For(role, Workspace);

        Assert.Equal(AgentReportTools.Review, policy.ReportToolName);
        Assert.True(policy.Allows(AgentCapability.ReadFiles));
        Assert.True(policy.Paths.CanRead($"{Worktree}/src/a.cs"));
        Assert.True(policy.Paths.CanRead($"{Notes}/notes.md"));
        Assert.False(policy.Allows(AgentCapability.UseBrowser));
    }

    [Fact]
    public void tester_uses_browser_and_run_commands_but_reports_tests_only()
    {
        RoleCapabilityPolicy policy = RoleCapabilityPolicies.For(AgentRole.Tester, new AgentWorkspace("/work/tests/run1"));

        Assert.Equal(AgentReportTools.Test, policy.ReportToolName);
        Assert.True(policy.Allows(AgentCapability.UseBrowser));
        Assert.True(policy.Allows(AgentCapability.RunShellCommands));
        Assert.True(policy.IsCommandAllowed("dotnet run --urls http://127.0.0.1:5123 &"));
        Assert.True(policy.IsCommandAllowed("playwright-cli open http://127.0.0.1:5123"));
        Assert.Equal("/work/tests/run1", policy.Paths.WorkingDirectory);
    }

    [Fact]
    public void conflict_resolver_edits_and_commits_in_its_worktree_only()
    {
        RoleCapabilityPolicy policy = RoleCapabilityPolicies.For(AgentRole.ConflictResolver, Workspace);

        Assert.Equal(AgentReportTools.ConflictResolution, policy.ReportToolName);
        Assert.True(policy.Allows(AgentCapability.WriteFiles));
        Assert.True(policy.Allows(AgentCapability.CreateLocalCommit));
        Assert.True(policy.Paths.CanWrite($"{Worktree}/src/a.cs"));
        Assert.False(policy.Paths.CanWrite("/work/trees/t2/src/a.cs"));
        Assert.True(policy.IsCommandAllowed("git add -A && git commit --no-edit"));
    }

    [Fact]
    public void explorer_writes_only_notes_outside_the_repository()
    {
        RoleCapabilityPolicy policy = RoleCapabilityPolicies.For(AgentRole.Explorer, new AgentWorkspace("/work/repos/octo/app", Notes));

        Assert.Equal(AgentReportTools.Exploration, policy.ReportToolName);
        Assert.Equal(GitHubTokenAccess.ReadOnlyIfRequired, policy.TokenAccess);
        Assert.True(policy.Allows(AgentCapability.WriteNotes));
        Assert.True(policy.Paths.CanWrite($"{Notes}/architecture.md"));
        Assert.False(policy.Paths.CanWrite("/work/repos/octo/app/notes.md"));
        Assert.True(policy.Paths.CanRead("/work/repos/octo/app/src/Program.cs"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/work/repos/octo/app/.notes")]
    public void explorer_requires_a_notes_directory_outside_the_repository(string? notesDirectory)
    {
        Assert.Throws<ArgumentException>(() =>
            RoleCapabilityPolicies.For(AgentRole.Explorer, new AgentWorkspace("/work/repos/octo/app", notesDirectory)));
    }

    [Theory]
    [MemberData(nameof(AllRoles))]
    public void agent_shells_never_inherit_github_tokens_or_credential_helpers(AgentRole role)
    {
        RoleCapabilityPolicy policy = RoleCapabilityPolicies.For(role, Workspace);
        var inherited = new Dictionary<string, string>
        {
            ["PATH"] = "/usr/bin",
            ["GH_TOKEN"] = "gho_x",
            ["github_token"] = "ghs_x",
            ["COPILOT_GITHUB_TOKEN"] = "ghs_y",
            ["GIT_ASKPASS"] = "/usr/lib/askpass",
        };

        IReadOnlyDictionary<string, string> environment = policy.BuildEnvironment(inherited);

        Assert.Equal("/usr/bin", environment["PATH"]);
        Assert.DoesNotContain(environment.Keys, key => key.Contains("TOKEN", StringComparison.OrdinalIgnoreCase));
        Assert.False(environment.ContainsKey("GIT_ASKPASS"));
        Assert.Equal("0", environment["GIT_TERMINAL_PROMPT"]);
        Assert.Equal("credential.helper", environment["GIT_CONFIG_KEY_0"]);
        Assert.Equal(string.Empty, environment["GIT_CONFIG_VALUE_0"]);
        Assert.Contains("GH_TOKEN", policy.ScrubbedEnvironmentVariables);
        Assert.Contains("GITHUB_TOKEN", policy.ScrubbedEnvironmentVariables);
        Assert.Contains("COPILOT_GITHUB_TOKEN", policy.ScrubbedEnvironmentVariables);
    }
}
