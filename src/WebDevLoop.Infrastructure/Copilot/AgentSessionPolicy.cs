using System.Text.RegularExpressions;
using WebDevLoop.Core.Agents;
using WebDevLoop.Infrastructure.Copilot.Runtime;

namespace WebDevLoop.Infrastructure.Copilot;

/// <summary>
/// Maps a <see cref="RoleCapabilityPolicy"/> onto a Copilot session: the tool allow-list and every permission decision.
/// Prompts are never trusted for authorization; anything the policy does not grant is denied.
/// </summary>
internal sealed partial class AgentSessionPolicy
{
    private readonly PathConfinement _skills;

    public AgentSessionPolicy(RoleCapabilityPolicy policy, string skillsRoot)
    {
        Policy = policy;
        _skills = new PathConfinement(policy.Paths.WorkingDirectory, [skillsRoot], []);
        Tools = new CopilotToolSelection(SelectBuiltInTools(policy), [policy.ReportToolName]);
    }

    public RoleCapabilityPolicy Policy { get; }

    public CopilotToolSelection Tools { get; }

    public AgentPermissionDecision Authorize(AgentPermissionRequest request) => request.Kind switch
    {
        AgentPermissionKind.Shell => AuthorizeShell(request.Target),
        AgentPermissionKind.Read => AuthorizeRead(request.Target),
        AgentPermissionKind.Write => AuthorizeWrite(request.Target),
        AgentPermissionKind.CustomTool when request.Target == Policy.ReportToolName => AgentPermissionDecision.Approve(),
        AgentPermissionKind.CustomTool => Deny($"Tool '{request.Target}' is not available to the {Policy.Role} role."),
        AgentPermissionKind.Url => Deny("Network access is not available to agents."),
        AgentPermissionKind.Mcp => Deny("MCP tools, including GitHub tools, are not available; WebDevLoop performs every GitHub operation."),
        _ => Deny($"Permission '{request.RawKind}' is not available to agents."),
    };

    private static IReadOnlyList<string> SelectBuiltInTools(RoleCapabilityPolicy policy)
    {
        var tools = new List<string>(CopilotBuiltInTools.Always);
        if (policy.Allows(AgentCapability.ReadFiles))
        {
            tools.AddRange(CopilotBuiltInTools.Read);
        }

        if (CanWriteFiles(policy))
        {
            tools.AddRange(CopilotBuiltInTools.Write);
        }

        if (policy.Allows(AgentCapability.RunShellCommands))
        {
            tools.AddRange(CopilotBuiltInTools.Shell);
        }

        return tools;
    }

    private static bool CanWriteFiles(RoleCapabilityPolicy policy) =>
        policy.Allows(AgentCapability.WriteFiles) || policy.Allows(AgentCapability.WriteNotes);

    private AgentPermissionDecision AuthorizeShell(string commandLine)
    {
        if (!Policy.Allows(AgentCapability.RunShellCommands))
        {
            return Deny($"The {Policy.Role} role may not run shell commands.");
        }

        if (Policy.FindDeniedCommand(commandLine) is { } denied)
        {
            return Deny($"'{denied}' is reserved for WebDevLoop; agents must not run it.");
        }

        if (ReferencedVariables(commandLine).FirstOrDefault(Policy.ScrubbedEnvironmentVariables.Contains) is { } variable)
        {
            return Deny($"Commands must not read the credential variable '{variable}'.");
        }

        return AgentPermissionDecision.Approve();
    }

    private AgentPermissionDecision AuthorizeRead(string path) =>
        Policy.Allows(AgentCapability.ReadFiles) && (Policy.Paths.CanRead(path) || _skills.CanRead(path))
            ? AgentPermissionDecision.Approve()
            : Deny($"Reading '{path}' is outside the {Policy.Role} role's directories.");

    private AgentPermissionDecision AuthorizeWrite(string path) =>
        CanWriteFiles(Policy) && Policy.Paths.CanWrite(path)
            ? AgentPermissionDecision.Approve()
            : Deny($"Writing '{path}' is outside the {Policy.Role} role's writable directories.");

    private static IEnumerable<string> ReferencedVariables(string commandLine) =>
        Identifier().Matches(commandLine).Select(match => match.Value);

    private static AgentPermissionDecision Deny(string reason) => AgentPermissionDecision.Deny(reason);

    [GeneratedRegex("[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex Identifier();
}
