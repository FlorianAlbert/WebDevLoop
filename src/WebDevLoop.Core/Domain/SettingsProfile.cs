namespace WebDevLoop.Core.Domain;

/// <summary>
/// Persisted settings row: the global profile (no repository) or a per-repository override.
/// Every value is nullable so resolution can fall through repo → global → embedded defaults.
/// </summary>
public sealed class SettingsProfile : VersionedEntity
{
    private readonly Dictionary<AgentRole, RoleSettingsOverride> _roles = [];

    private SettingsProfile()
    {
    }

    public int Id { get; private set; }

    public int? RepositoryId { get; private set; }

    public bool IsGlobal => RepositoryId is null;

    public string? WorkspaceRootDirectory { get; set; }

    public string? CopilotBaseDirectory { get; set; }

    public BranchName? BaseBranch { get; set; }

    public int? MaxActiveSpecsPerRepo { get; set; }

    public SpecDependencyMode? SpecDependencyMode { get; set; }

    public int? MaxConcurrentImplementersGlobal { get; set; }

    public int? MaxConcurrentImplementersPerRepo { get; set; }

    public int? MaxReviewIterations { get; set; }

    public int? MaxRetries { get; set; }

    public int? ParentReviewCycleLimit { get; set; }

    public int? TesterCycleLimit { get; set; }

    public string? TesterRunInstructions { get; set; }

    public TestPortRange? TestPortRange { get; set; }

    public IReadOnlyDictionary<AgentRole, RoleSettingsOverride> Roles => _roles;

    public static SettingsProfile ForGlobal() => new();

    public static SettingsProfile ForRepository(int repositoryId) => new() { RepositoryId = repositoryId };

    public void SetRole(AgentRole role, RoleSettingsOverride settings) => _roles[role] = settings;
}
