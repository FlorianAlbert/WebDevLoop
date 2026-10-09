using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Agents;

/// <summary>The role → capability table from the architecture. App ports, never agents, perform Git/GitHub publishing.</summary>
public static class RoleCapabilityPolicies
{
    private static readonly DeniedCommand[] PublishingCommands =
    [
        new("gh"),
        new("git", "push"),
        new("git", "fetch"),
        new("git", "pull"),
        new("git", "clone"),
        new("git", "remote"),
        new("git", "worktree"),
        new("git", "credential"),
        new("git", "config"),
        new("git", "update-ref"),
        new("git", "branch"),
        new("git", "switch", "-c"),
        new("git", "switch", "-C"),
        new("git", "switch", "--create"),
        new("git", "checkout", "-b"),
        new("git", "checkout", "-B"),
        new("git", "checkout", "--orphan"),
    ];

    private static readonly DeniedCommand[] RepositoryMutationCommands =
    [
        .. PublishingCommands,
        .. new[]
        {
            "add", "am", "apply", "checkout", "cherry-pick", "clean", "commit", "gc", "merge", "mv", "notes", "prune",
            "rebase", "replace", "reset", "restore", "revert", "rm", "stash", "switch", "tag",
        }.Select(subcommand => new DeniedCommand("git", subcommand)),
    ];

    /// <summary>Removed from agent shells; Copilot auth is supplied to the runtime, not to tools the agent runs.</summary>
    private static readonly string[] ScrubbedVariables =
    [
        "GH_TOKEN",
        "GITHUB_TOKEN",
        "COPILOT_GITHUB_TOKEN",
        "GH_ENTERPRISE_TOKEN",
        "GITHUB_ENTERPRISE_TOKEN",
        "GITHUB_COPILOT_API_TOKEN",
        "GIT_ASKPASS",
        "SSH_ASKPASS",
        "SSH_AUTH_SOCK",
        "GIT_CONFIG_PARAMETERS",
    ];

    /// <summary>Disables Git credential helpers and interactive credential prompts.</summary>
    private static readonly Dictionary<string, string> CredentialLockdown = new(StringComparer.Ordinal)
    {
        ["GIT_TERMINAL_PROMPT"] = "0",
        ["GCM_INTERACTIVE"] = "never",
        ["GIT_CONFIG_COUNT"] = "1",
        ["GIT_CONFIG_KEY_0"] = "credential.helper",
        ["GIT_CONFIG_VALUE_0"] = string.Empty,
    };

    private static readonly AgentCapability[] LocalEditing =
    [
        AgentCapability.ReadFiles,
        AgentCapability.WriteFiles,
        AgentCapability.RunShellCommands,
        AgentCapability.CreateLocalCommit,
        AgentCapability.ReportResult,
    ];

    private static readonly AgentCapability[] ReadOnlyInspection =
    [
        AgentCapability.ReadFiles,
        AgentCapability.RunShellCommands,
        AgentCapability.ReportResult,
    ];

    public static RoleCapabilityPolicy For(AgentRole role, AgentWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        string workingDirectory = workspace.WorkingDirectory;
        string[] readable = workspace.NotesDirectory is { } notes ? [workingDirectory, notes] : [workingDirectory];

        return role switch
        {
            AgentRole.Explorer => Create(
                role,
                [AgentCapability.ReadFiles, AgentCapability.WriteNotes, AgentCapability.RunShellCommands, AgentCapability.ReportResult],
                new PathConfinement(workingDirectory, readable, [RequireNotesOutsideRepository(workspace)]),
                RepositoryMutationCommands,
                GitHubTokenAccess.ReadOnlyIfRequired,
                AgentReportTools.Exploration),
            AgentRole.Implementer => Create(
                role,
                LocalEditing,
                new PathConfinement(workingDirectory, readable, [workingDirectory]),
                PublishingCommands,
                GitHubTokenAccess.ReadOnlyIfRequired,
                AgentReportTools.Implementation),
            AgentRole.ReviewerCodingStandards or AgentRole.ReviewerSpecification => Create(
                role,
                ReadOnlyInspection,
                new PathConfinement(workingDirectory, readable, []),
                RepositoryMutationCommands,
                GitHubTokenAccess.None,
                AgentReportTools.Review),
            AgentRole.ConflictResolver => Create(
                role,
                LocalEditing,
                new PathConfinement(workingDirectory, readable, [workingDirectory]),
                PublishingCommands,
                GitHubTokenAccess.None,
                AgentReportTools.ConflictResolution),
            AgentRole.Tester => Create(
                role,
                [.. ReadOnlyInspection, AgentCapability.UseBrowser],
                new PathConfinement(workingDirectory, readable, []),
                RepositoryMutationCommands,
                GitHubTokenAccess.None,
                AgentReportTools.Test),
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown agent role."),
        };
    }

    private static string RequireNotesOutsideRepository(AgentWorkspace workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace.NotesDirectory))
        {
            throw new ArgumentException("The explorer needs an app-allocated notes directory.", nameof(workspace));
        }

        if (PathConfinement.IsUnder(workspace.NotesDirectory, workspace.WorkingDirectory))
        {
            throw new ArgumentException("The exploration notes directory must be outside the repository.", nameof(workspace));
        }

        return workspace.NotesDirectory;
    }

    private static RoleCapabilityPolicy Create(
        AgentRole role,
        AgentCapability[] capabilities,
        PathConfinement paths,
        DeniedCommand[] deniedCommands,
        GitHubTokenAccess tokenAccess,
        string reportTool) =>
        new(
            role,
            capabilities.ToHashSet(),
            paths,
            deniedCommands,
            ScrubbedVariables.ToHashSet(StringComparer.OrdinalIgnoreCase),
            CredentialLockdown,
            tokenAccess,
            reportTool);
}
