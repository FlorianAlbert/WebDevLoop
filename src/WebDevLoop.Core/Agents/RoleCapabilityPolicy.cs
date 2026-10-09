using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Agents;

/// <summary>
/// Authorization boundary for one agent role. Prompts are not trusted for authorization: the agent runner maps this policy to
/// tool filters, permission decisions, environment scrubbing, working-directory confinement, and token selection.
/// </summary>
public sealed class RoleCapabilityPolicy
{
    internal RoleCapabilityPolicy(
        AgentRole role,
        IReadOnlySet<AgentCapability> allowedCapabilities,
        PathConfinement paths,
        IReadOnlyList<DeniedCommand> deniedCommands,
        IReadOnlySet<string> scrubbedEnvironmentVariables,
        IReadOnlyDictionary<string, string> environmentOverrides,
        GitHubTokenAccess tokenAccess,
        string reportToolName)
    {
        Role = role;
        AllowedCapabilities = allowedCapabilities;
        Paths = paths;
        DeniedCommands = deniedCommands;
        ScrubbedEnvironmentVariables = scrubbedEnvironmentVariables;
        EnvironmentOverrides = environmentOverrides;
        TokenAccess = tokenAccess;
        ReportToolName = reportToolName;
    }

    public AgentRole Role { get; }

    public IReadOnlySet<AgentCapability> AllowedCapabilities { get; }

    public PathConfinement Paths { get; }

    public IReadOnlyList<DeniedCommand> DeniedCommands { get; }

    /// <summary>Variables removed from every shell the agent spawns (matched case-insensitively).</summary>
    public IReadOnlySet<string> ScrubbedEnvironmentVariables { get; }

    /// <summary>Variables forced into every shell, e.g. to disable Git credential helpers and prompts.</summary>
    public IReadOnlyDictionary<string, string> EnvironmentOverrides { get; }

    public GitHubTokenAccess TokenAccess { get; }

    public bool RequiresGitHubWriteToken => TokenAccess == GitHubTokenAccess.Write;

    /// <summary>Terminal, permission-free custom tool through which the agent returns its structured report.</summary>
    public string ReportToolName { get; }

    public bool Allows(AgentCapability capability) => AllowedCapabilities.Contains(capability);

    /// <summary>The first denied command found anywhere in <paramref name="commandLine"/>, or null when it may run.</summary>
    public DeniedCommand? FindDeniedCommand(string commandLine) => ShellCommandGuard.FindDenied(commandLine, DeniedCommands);

    public bool IsCommandAllowed(string commandLine) => FindDeniedCommand(commandLine) is null;

    /// <summary>The environment for agent shells: <paramref name="inherited"/> minus scrubbed variables, plus overrides.</summary>
    public IReadOnlyDictionary<string, string> BuildEnvironment(IReadOnlyDictionary<string, string> inherited)
    {
        ArgumentNullException.ThrowIfNull(inherited);
        var environment = inherited
            .Where(variable => !ScrubbedEnvironmentVariables.Contains(variable.Key))
            .ToDictionary(variable => variable.Key, variable => variable.Value, StringComparer.Ordinal);

        foreach ((string name, string value) in EnvironmentOverrides)
        {
            environment[name] = value;
        }

        return environment;
    }
}
