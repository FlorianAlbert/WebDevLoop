using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Web.Components.Steps;

namespace WebDevLoop.Web.Tests.Components.Steps;

public sealed class RolePolicySummaryTests
{
    public static TheoryData<AgentRole> Roles => [.. Enum.GetValues<AgentRole>()];

    [Theory]
    [MemberData(nameof(Roles))]
    public void Every_role_has_a_summary_with_a_report_tool(AgentRole role)
    {
        RolePolicySummary summary = RolePolicySummary.For(role);

        Assert.Equal(role, summary.Role);
        Assert.Contains(AgentCapability.ReportResult, summary.AllowedCapabilities);
        Assert.False(string.IsNullOrWhiteSpace(summary.ReportTool));
    }

    [Fact]
    public void Implementer_may_commit_locally_but_not_push()
    {
        RolePolicySummary summary = RolePolicySummary.For(AgentRole.Implementer);

        Assert.Contains(AgentCapability.CreateLocalCommit, summary.AllowedCapabilities);
        Assert.Contains("git push", summary.DeniedCommands);
        Assert.DoesNotContain(AgentCapability.PushRefs, summary.AllowedCapabilities);
    }

    [Fact]
    public void Reviewer_is_read_only_without_github_token()
    {
        RolePolicySummary summary = RolePolicySummary.For(AgentRole.ReviewerSpecification);

        Assert.DoesNotContain(AgentCapability.WriteFiles, summary.AllowedCapabilities);
        Assert.Equal(GitHubTokenAccess.None, summary.TokenAccess);
    }
}
