using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Infrastructure.Copilot;
using WebDevLoop.Infrastructure.Copilot.Runtime;

namespace WebDevLoop.Infrastructure.Tests.Copilot;

public sealed class AgentSessionPolicyTests
{
    private const string Worktree = "/work/trees/t1";
    private const string Notes = "/work/notes/run1";
    private const string SkillsRoot = "/app/skills";

    public static TheoryData<AgentRole> AllRoles => [.. Enum.GetValues<AgentRole>()];

    [Fact]
    public void implementer_denies_git_push_and_github_tools_while_allowing_local_edit_test_and_report()
    {
        AgentSessionPolicy policy = For(AgentRole.Implementer);

        Assert.Superset(new HashSet<string> { "view", "grep", "glob", "edit", "create", "bash", "skill" }, policy.Tools.BuiltInTools.ToHashSet());
        Assert.Equal([AgentReportTools.Implementation], policy.Tools.CustomTools);

        AssertDenied(policy, Shell("git push origin HEAD"), "git push");
        AssertDenied(policy, Shell("git -C /work/trees/t1 push --force"), "git push");
        AssertDenied(policy, Shell("gh pr create --fill"), "gh");
        AssertDenied(policy, Shell("dotnet test && gh issue create -t x"), "gh");
        AssertDenied(policy, new AgentPermissionRequest(AgentPermissionKind.Mcp, "github-mcp-server/create_issue", "mcp"));
        AssertDenied(policy, new AgentPermissionRequest(AgentPermissionKind.CustomTool, "create_pull_request", "custom-tool"));

        AssertApproved(policy, Shell("dotnet test"));
        AssertApproved(policy, Shell("git add -A && git commit -m \"Fix #12\""));
        AssertApproved(policy, Write($"{Worktree}/src/Parser.cs"));
        AssertApproved(policy, Write("src/Relative.cs"));
        AssertApproved(policy, Read($"{Notes}/README.md"));
        AssertApproved(policy, Read($"{SkillsRoot}/tdd/tests.md"));
        AssertApproved(policy, new AgentPermissionRequest(AgentPermissionKind.CustomTool, AgentReportTools.Implementation, "custom-tool"));
    }

    [Fact]
    public void implementer_cannot_write_outside_its_worktree()
    {
        AgentSessionPolicy policy = For(AgentRole.Implementer);

        AssertDenied(policy, Write($"{Notes}/README.md"));
        AssertDenied(policy, Write("../t2/src/a.cs"));
        AssertDenied(policy, Write($"{SkillsRoot}/tdd/SKILL.md"));
        AssertDenied(policy, Read("/etc/shadow"));
    }

    [Theory]
    [InlineData(AgentRole.ReviewerCodingStandards)]
    [InlineData(AgentRole.ReviewerSpecification)]
    public void reviewers_get_read_only_tools_and_cannot_write_or_commit(AgentRole role)
    {
        AgentSessionPolicy policy = For(role);

        Assert.DoesNotContain("edit", policy.Tools.BuiltInTools);
        Assert.DoesNotContain("create", policy.Tools.BuiltInTools);
        Assert.Contains("bash", policy.Tools.BuiltInTools);
        Assert.Equal([AgentReportTools.Review], policy.Tools.CustomTools);
        AssertDenied(policy, Write($"{Worktree}/src/a.cs"));
        AssertDenied(policy, Shell("git commit -am wip"));
        AssertApproved(policy, Shell("git diff main...HEAD"));
        AssertApproved(policy, Read($"{Worktree}/src/a.cs"));
        AssertApproved(policy, Read($"{Notes}/README.md"));
    }

    [Fact]
    public void tester_writes_evidence_only_and_may_run_the_application()
    {
        AgentSessionPolicy policy = For(AgentRole.Tester);

        Assert.Contains("create", policy.Tools.BuiltInTools);
        AssertApproved(policy, Write($"{Notes}/test-evidence/attempt-1/home.png"));
        AssertDenied(policy, Write($"{Worktree}/src/a.cs"));
        AssertApproved(policy, Shell("dotnet run --urls http://127.0.0.1:5123 > /work/notes/run1/test-evidence/attempt-1/app.log 2>&1 &"));
        AssertApproved(policy, Shell("playwright-cli -s=webdevloop-run1 open http://127.0.0.1:5123"));
        AssertApproved(policy, Read($"{SkillsRoot}/playwright-cli/SKILL.md"));
    }

    [Fact]
    public void explorer_writes_notes_but_not_the_repository()
    {
        AgentSessionPolicy policy = For(AgentRole.Explorer, new AgentWorkspace("/work/repos/octo/app", Notes));

        AssertApproved(policy, Write($"{Notes}/README.md"));
        AssertDenied(policy, Write("/work/repos/octo/app/README.md"));
        AssertApproved(policy, Read("/work/repos/octo/app/src/Program.cs"));
    }

    [Theory]
    [MemberData(nameof(AllRoles))]
    public void no_role_gets_mcp_web_or_unknown_permissions(AgentRole role)
    {
        AgentSessionPolicy policy = For(role);

        Assert.DoesNotContain("web_fetch", policy.Tools.BuiltInTools);
        Assert.DoesNotContain("task", policy.Tools.BuiltInTools);
        Assert.DoesNotContain("ask_user", policy.Tools.BuiltInTools);
        AssertDenied(policy, new AgentPermissionRequest(AgentPermissionKind.Url, "https://api.github.com/repos/o/r/pulls", "url"));
        AssertDenied(policy, new AgentPermissionRequest(AgentPermissionKind.Mcp, "github-mcp-server/list_issues", "mcp"));
        AssertDenied(policy, new AgentPermissionRequest(AgentPermissionKind.Other, "store fact", "memory"));
    }

    [Theory]
    [MemberData(nameof(AllRoles))]
    public void shell_commands_that_read_scrubbed_credentials_are_denied(AgentRole role)
    {
        AgentSessionPolicy policy = For(role);

        AssertDenied(policy, Shell("echo $COPILOT_GITHUB_TOKEN"));
        AssertDenied(policy, Shell("printenv GH_TOKEN"));
        AssertDenied(policy, Shell("curl -H \"Authorization: token ${github_token}\" https://api.github.com"));
    }

    private static AgentSessionPolicy For(AgentRole role, AgentWorkspace? workspace = null) =>
        new(RoleCapabilityPolicies.For(role, workspace ?? new AgentWorkspace(Worktree, Notes)), SkillsRoot);

    private static AgentPermissionRequest Shell(string command) => new(AgentPermissionKind.Shell, command, "shell");

    private static AgentPermissionRequest Write(string path) => new(AgentPermissionKind.Write, path, "write");

    private static AgentPermissionRequest Read(string path) => new(AgentPermissionKind.Read, path, "read");

    private static void AssertApproved(AgentSessionPolicy policy, AgentPermissionRequest request)
    {
        AgentPermissionDecision decision = policy.Authorize(request);
        Assert.True(decision.IsApproved, $"{request.Kind} '{request.Target}' should be approved but was denied: {decision.Reason}");
    }

    private static void AssertDenied(AgentSessionPolicy policy, AgentPermissionRequest request, string? reasonContains = null)
    {
        AgentPermissionDecision decision = policy.Authorize(request);
        Assert.False(decision.IsApproved, $"{request.Kind} '{request.Target}' should be denied.");
        Assert.False(string.IsNullOrWhiteSpace(decision.Reason));
        if (reasonContains is not null)
        {
            Assert.Contains(reasonContains, decision.Reason);
        }
    }
}
