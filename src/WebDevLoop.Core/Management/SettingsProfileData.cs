using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Management;

/// <summary>
/// Editable form of one persisted settings layer. Every value is optional: <c>null</c> means "not overridden here" and
/// falls through repo → global → embedded defaults during resolution. A PUT replaces the whole layer.
/// </summary>
public sealed record SettingsProfileData
{
    public string? WorkspaceRootDirectory { get; init; }

    public string? CopilotBaseDirectory { get; init; }

    public string? BaseBranch { get; init; }

    public int? MaxActiveSpecsPerRepo { get; init; }

    public SpecDependencyMode? SpecDependencyMode { get; init; }

    public int? MaxConcurrentImplementersGlobal { get; init; }

    public int? MaxConcurrentImplementersPerRepo { get; init; }

    public int? MaxReviewIterations { get; init; }

    public int? MaxRetries { get; init; }

    public int? ParentReviewCycleLimit { get; init; }

    public int? TesterCycleLimit { get; init; }

    public string? TesterRunInstructions { get; init; }

    public PortRangeData? TestPortRange { get; init; }

    public IReadOnlyDictionary<AgentRole, RoleSettingsOverride>? Roles { get; init; }
}
