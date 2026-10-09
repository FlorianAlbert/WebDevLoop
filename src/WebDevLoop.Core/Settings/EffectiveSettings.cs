using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Settings;

/// <summary>Fully resolved settings for one repository; every value is present.</summary>
public sealed record EffectiveSettings
{
    public required string WorkspaceRootDirectory { get; init; }

    public required string CopilotBaseDirectory { get; init; }

    public required BranchName BaseBranch { get; init; }

    public required int MaxActiveSpecsPerRepo { get; init; }

    public required SpecDependencyMode SpecDependencyMode { get; init; }

    public required int MaxConcurrentImplementersGlobal { get; init; }

    public required int MaxConcurrentImplementersPerRepo { get; init; }

    public required int MaxReviewIterations { get; init; }

    public required int MaxRetries { get; init; }

    public required int ParentReviewCycleLimit { get; init; }

    public required int TesterCycleLimit { get; init; }

    public required string TesterRunInstructions { get; init; }

    public required TestPortRange TestPortRange { get; init; }

    public required bool PatFallbackEnabled { get; init; }

    public required IReadOnlyDictionary<AgentRole, RoleSettings> Roles { get; init; }

    public RoleSettings For(AgentRole role) =>
        Roles.TryGetValue(role, out RoleSettings? settings)
            ? settings
            : throw new KeyNotFoundException($"No settings resolved for agent role '{role}'.");
}
