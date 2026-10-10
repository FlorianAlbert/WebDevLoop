using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Management;

/// <summary>
/// First start: copies the embedded defaults (including the default prompt templates) into the required global settings
/// profile, so users edit real values instead of invisible defaults. An existing global profile is never touched.
/// </summary>
public sealed class GlobalSettingsSeeder(ISettingsProfileRepository profiles, IUnitOfWork unitOfWork, EffectiveSettings embeddedDefaults)
{
    /// <returns>Whether the global profile was created.</returns>
    public async Task<bool> SeedAsync(CancellationToken cancellationToken)
    {
        if (await profiles.GetGlobalAsync(cancellationToken) is not null)
        {
            return false;
        }

        profiles.Add(CreateGlobal(embeddedDefaults));
        return await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved;
    }

    private static SettingsProfile CreateGlobal(EffectiveSettings defaults)
    {
        SettingsProfile global = SettingsProfile.ForGlobal();
        global.WorkspaceRootDirectory = defaults.WorkspaceRootDirectory;
        global.CopilotBaseDirectory = defaults.CopilotBaseDirectory;
        global.BaseBranch = defaults.BaseBranch;
        global.MaxActiveSpecsPerRepo = defaults.MaxActiveSpecsPerRepo;
        global.SpecDependencyMode = defaults.SpecDependencyMode;
        global.MaxConcurrentImplementersGlobal = defaults.MaxConcurrentImplementersGlobal;
        global.MaxConcurrentImplementersPerRepo = defaults.MaxConcurrentImplementersPerRepo;
        global.MaxReviewIterations = defaults.MaxReviewIterations;
        global.MaxRetries = defaults.MaxRetries;
        global.ParentReviewCycleLimit = defaults.ParentReviewCycleLimit;
        global.TesterCycleLimit = defaults.TesterCycleLimit;
        global.TroubleshooterEnabled = defaults.TroubleshooterEnabled;
        global.TroubleshooterMaxAttempts = defaults.TroubleshooterMaxAttempts;
        global.TesterRunInstructions = defaults.TesterRunInstructions;
        global.TestPortRange = defaults.TestPortRange;
        foreach ((AgentRole role, RoleSettings settings) in defaults.Roles)
        {
            global.SetRole(role, new RoleSettingsOverride(
                settings.Model, settings.ReasoningEffort, settings.PromptTemplate, (int)settings.Timeout.TotalSeconds));
        }

        return global;
    }
}
