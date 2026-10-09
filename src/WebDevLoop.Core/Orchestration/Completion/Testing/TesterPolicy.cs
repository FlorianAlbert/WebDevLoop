using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Completion.Testing;

/// <summary>
/// The tester's capability policy (<see cref="RoleCapabilityPolicies"/>) plus the test target's variables (reserved port,
/// app URL, lease marker) forced into every shell the tester spawns. Target variables can neither re-add scrubbed
/// credentials nor override the policy's credential lockdown.
/// </summary>
internal static class TesterPolicy
{
    public static RoleCapabilityPolicy For(TestWorkspace workspace, TestTarget target)
    {
        RoleCapabilityPolicy tester = RoleCapabilityPolicies.For(AgentRole.Tester, new AgentWorkspace(workspace.CheckoutDirectory, workspace.NotesDirectory));
        var environment = new Dictionary<string, string>(tester.EnvironmentOverrides, StringComparer.Ordinal);
        foreach ((string name, string value) in target.Environment)
        {
            if (!tester.ScrubbedEnvironmentVariables.Contains(name) && !tester.EnvironmentOverrides.ContainsKey(name))
            {
                environment[name] = value;
            }
        }

        return new RoleCapabilityPolicy(
            tester.Role,
            tester.AllowedCapabilities,
            tester.Paths,
            tester.DeniedCommands,
            tester.ScrubbedEnvironmentVariables,
            environment,
            tester.TokenAccess,
            tester.ReportToolName);
    }
}
