using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Management;

public sealed record EffectiveSettingsView(
    string WorkspaceRootDirectory,
    string CopilotBaseDirectory,
    string BaseBranch,
    int MaxActiveSpecsPerRepo,
    SpecDependencyMode SpecDependencyMode,
    int MaxConcurrentImplementersGlobal,
    int MaxConcurrentImplementersPerRepo,
    int MaxReviewIterations,
    int MaxRetries,
    int ParentReviewCycleLimit,
    int TesterCycleLimit,
    string TesterRunInstructions,
    PortRangeData TestPortRange,
    IReadOnlyDictionary<AgentRole, EffectiveRoleSettingsView> Roles,
    bool TroubleshooterEnabled,
    int TroubleshooterMaxAttempts);

public sealed record EffectiveRoleSettingsView(string Model, string ReasoningEffort, string PromptTemplate, int TimeoutSeconds);
