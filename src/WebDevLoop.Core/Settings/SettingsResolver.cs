using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Settings;

/// <summary>Resolves effective settings: repository override → global → embedded defaults.</summary>
public sealed class SettingsResolver(EffectiveSettings embeddedDefaults)
{
    private static readonly RoleSettingsOverride Inherit = new();

    public EffectiveSettings Resolve(SettingsProfile global, SettingsProfile? repository = null)
    {
        ArgumentNullException.ThrowIfNull(global);
        if (!global.IsGlobal)
        {
            throw new ArgumentException("Expected the global settings profile.", nameof(global));
        }

        if (repository is { IsGlobal: true })
        {
            throw new ArgumentException("Expected a repository settings profile.", nameof(repository));
        }

        return new EffectiveSettings
        {
            WorkspaceRootDirectory = FirstText(repository?.WorkspaceRootDirectory, global.WorkspaceRootDirectory, embeddedDefaults.WorkspaceRootDirectory),
            CopilotBaseDirectory = FirstText(repository?.CopilotBaseDirectory, global.CopilotBaseDirectory, embeddedDefaults.CopilotBaseDirectory),
            BaseBranch = repository?.BaseBranch ?? global.BaseBranch ?? embeddedDefaults.BaseBranch,
            MaxActiveSpecsPerRepo = repository?.MaxActiveSpecsPerRepo ?? global.MaxActiveSpecsPerRepo ?? embeddedDefaults.MaxActiveSpecsPerRepo,
            SpecDependencyMode = repository?.SpecDependencyMode ?? global.SpecDependencyMode ?? embeddedDefaults.SpecDependencyMode,
            // Global-only: a repository row never raises or lowers the process-wide implementer cap.
            MaxConcurrentImplementersGlobal = global.MaxConcurrentImplementersGlobal ?? embeddedDefaults.MaxConcurrentImplementersGlobal,
            MaxConcurrentImplementersPerRepo = repository?.MaxConcurrentImplementersPerRepo ?? global.MaxConcurrentImplementersPerRepo ?? embeddedDefaults.MaxConcurrentImplementersPerRepo,
            MaxReviewIterations = repository?.MaxReviewIterations ?? global.MaxReviewIterations ?? embeddedDefaults.MaxReviewIterations,
            MaxRetries = repository?.MaxRetries ?? global.MaxRetries ?? embeddedDefaults.MaxRetries,
            ParentReviewCycleLimit = repository?.ParentReviewCycleLimit ?? global.ParentReviewCycleLimit ?? embeddedDefaults.ParentReviewCycleLimit,
            TesterCycleLimit = repository?.TesterCycleLimit ?? global.TesterCycleLimit ?? embeddedDefaults.TesterCycleLimit,
            TesterRunInstructions = FirstText(repository?.TesterRunInstructions, global.TesterRunInstructions, embeddedDefaults.TesterRunInstructions),
            TestPortRange = repository?.TestPortRange ?? global.TestPortRange ?? embeddedDefaults.TestPortRange,
            PatFallbackEnabled = repository?.PatFallbackEnabled ?? global.PatFallbackEnabled ?? embeddedDefaults.PatFallbackEnabled,
            Roles = Enum.GetValues<AgentRole>().ToDictionary(role => role, role => ResolveRole(role, global, repository)),
        };
    }

    private RoleSettings ResolveRole(AgentRole role, SettingsProfile global, SettingsProfile? repository)
    {
        RoleSettings fallback = embeddedDefaults.For(role);
        RoleSettingsOverride globalRole = global.Roles.GetValueOrDefault(role) ?? Inherit;
        RoleSettingsOverride repositoryRole = repository?.Roles.GetValueOrDefault(role) ?? Inherit;
        int? timeoutSeconds = repositoryRole.TimeoutSeconds ?? globalRole.TimeoutSeconds;

        return new RoleSettings(
            FirstText(repositoryRole.Model, globalRole.Model, fallback.Model),
            FirstText(repositoryRole.ReasoningEffort, globalRole.ReasoningEffort, fallback.ReasoningEffort),
            FirstText(repositoryRole.PromptTemplate, globalRole.PromptTemplate, fallback.PromptTemplate),
            timeoutSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : fallback.Timeout);
    }

    // Blank text is treated as "not set" so an emptied UI field never masks the inherited value.
    private static string FirstText(string? repository, string? global, string fallback) =>
        !string.IsNullOrWhiteSpace(repository) ? repository
        : !string.IsNullOrWhiteSpace(global) ? global
        : fallback;
}
