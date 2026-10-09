using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Web.Components.Steps;

public sealed record RolePolicySummary(
    AgentRole Role,
    IReadOnlyList<AgentCapability> AllowedCapabilities,
    IReadOnlyList<string> DeniedCommands,
    GitHubTokenAccess TokenAccess,
    string ReportTool)
{
    // The summary never shows paths; these only satisfy the policy factory's confinement validation.
    private static readonly AgentWorkspace DisplayWorkspace = new(
        Path.GetFullPath(Path.Combine(Path.DirectorySeparatorChar.ToString(), "workspace", "worktree")),
        Path.GetFullPath(Path.Combine(Path.DirectorySeparatorChar.ToString(), "workspace", "notes")));

    public static RolePolicySummary For(AgentRole role)
    {
        RoleCapabilityPolicy policy = RoleCapabilityPolicies.For(role, DisplayWorkspace);
        return new RolePolicySummary(
            role,
            policy.AllowedCapabilities.Order().ToList(),
            policy.DeniedCommands.Select(command => command.ToString()).ToList(),
            policy.TokenAccess,
            policy.ReportToolName);
    }
}
