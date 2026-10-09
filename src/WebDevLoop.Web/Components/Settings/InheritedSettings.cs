using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Web.Components.Settings;

/// <summary>The values a settings layer inherits when it leaves a field unset, mirroring the Core resolution order.</summary>
public static class InheritedSettings
{
    /// <summary>What the global layer inherits: the embedded defaults.</summary>
    public static EffectiveSettingsView FromDefaults(EffectiveSettings defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        return new EffectiveSettingsView(
            defaults.WorkspaceRootDirectory,
            defaults.CopilotBaseDirectory,
            defaults.BaseBranch.Value,
            defaults.MaxActiveSpecsPerRepo,
            defaults.SpecDependencyMode,
            defaults.MaxConcurrentImplementersGlobal,
            defaults.MaxConcurrentImplementersPerRepo,
            defaults.MaxReviewIterations,
            defaults.MaxRetries,
            defaults.ParentReviewCycleLimit,
            defaults.TesterCycleLimit,
            defaults.TesterRunInstructions,
            new PortRangeData(defaults.TestPortRange.Start, defaults.TestPortRange.End),
            defaults.PatFallbackEnabled,
            defaults.Roles.ToDictionary(
                entry => entry.Key,
                entry => new EffectiveRoleSettingsView(
                    entry.Value.Model,
                    entry.Value.ReasoningEffort,
                    entry.Value.PromptTemplate,
                    (int)entry.Value.Timeout.TotalSeconds)));
    }

    /// <summary>What a repository inherits: the global layer over the embedded defaults.</summary>
    public static EffectiveSettingsView UnderRepository(SettingsProfileData global, EffectiveSettings defaults)
    {
        ArgumentNullException.ThrowIfNull(global);
        EffectiveSettingsView baseline = FromDefaults(defaults);
        return baseline with
        {
            WorkspaceRootDirectory = Text(global.WorkspaceRootDirectory, baseline.WorkspaceRootDirectory),
            CopilotBaseDirectory = Text(global.CopilotBaseDirectory, baseline.CopilotBaseDirectory),
            BaseBranch = Text(global.BaseBranch, baseline.BaseBranch),
            MaxActiveSpecsPerRepo = global.MaxActiveSpecsPerRepo ?? baseline.MaxActiveSpecsPerRepo,
            SpecDependencyMode = global.SpecDependencyMode ?? baseline.SpecDependencyMode,
            MaxConcurrentImplementersGlobal = global.MaxConcurrentImplementersGlobal ?? baseline.MaxConcurrentImplementersGlobal,
            MaxConcurrentImplementersPerRepo = global.MaxConcurrentImplementersPerRepo ?? baseline.MaxConcurrentImplementersPerRepo,
            MaxReviewIterations = global.MaxReviewIterations ?? baseline.MaxReviewIterations,
            MaxRetries = global.MaxRetries ?? baseline.MaxRetries,
            ParentReviewCycleLimit = global.ParentReviewCycleLimit ?? baseline.ParentReviewCycleLimit,
            TesterCycleLimit = global.TesterCycleLimit ?? baseline.TesterCycleLimit,
            TesterRunInstructions = Text(global.TesterRunInstructions, baseline.TesterRunInstructions),
            TestPortRange = global.TestPortRange ?? baseline.TestPortRange,
            PatFallbackEnabled = global.PatFallbackEnabled ?? baseline.PatFallbackEnabled,
            Roles = baseline.Roles.ToDictionary(entry => entry.Key, entry => Role(global.Roles?.GetValueOrDefault(entry.Key), entry.Value)),
        };
    }

    // Blank text counts as "not set", exactly like SettingsResolver.
    private static string Text(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static EffectiveRoleSettingsView Role(RoleSettingsOverride? global, EffectiveRoleSettingsView fallback) =>
        global is null
            ? fallback
            : new EffectiveRoleSettingsView(
                Text(global.Model, fallback.Model),
                Text(global.ReasoningEffort, fallback.ReasoningEffort),
                Text(global.PromptTemplate, fallback.PromptTemplate),
                global.TimeoutSeconds ?? fallback.TimeoutSeconds);
}
