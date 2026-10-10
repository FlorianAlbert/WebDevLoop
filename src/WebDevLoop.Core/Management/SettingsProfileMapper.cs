using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Management;

internal static class SettingsProfileMapper
{
    private static readonly RoleSettingsOverride Unset = new();

    /// <summary>Writes every value of <paramref name="data"/> to <paramref name="target"/>, replacing the previous layer. Unparseable values are reported, not applied.</summary>
    public static IReadOnlyList<SettingsValidationError> Apply(SettingsProfileData data, SettingsProfile target)
    {
        var errors = new List<SettingsValidationError>();
        target.WorkspaceRootDirectory = data.WorkspaceRootDirectory;
        target.CopilotBaseDirectory = data.CopilotBaseDirectory;
        target.BaseBranch = ParseBranch(data.BaseBranch, errors);
        target.MaxActiveSpecsPerRepo = data.MaxActiveSpecsPerRepo;
        target.SpecDependencyMode = data.SpecDependencyMode;
        target.MaxConcurrentImplementersGlobal = data.MaxConcurrentImplementersGlobal;
        target.MaxConcurrentImplementersPerRepo = data.MaxConcurrentImplementersPerRepo;
        target.MaxReviewIterations = data.MaxReviewIterations;
        target.MaxRetries = data.MaxRetries;
        target.ParentReviewCycleLimit = data.ParentReviewCycleLimit;
        target.TesterCycleLimit = data.TesterCycleLimit;
        target.TroubleshooterEnabled = data.TroubleshooterEnabled;
        target.TroubleshooterMaxAttempts = data.TroubleshooterMaxAttempts;
        target.TesterRunInstructions = data.TesterRunInstructions;
        target.TestPortRange = ParsePortRange(data.TestPortRange, errors);
        ApplyRoles(data.Roles, target);
        return errors;
    }

    public static SettingsProfileData ToData(SettingsProfile profile) => new()
    {
        WorkspaceRootDirectory = profile.WorkspaceRootDirectory,
        CopilotBaseDirectory = profile.CopilotBaseDirectory,
        BaseBranch = profile.BaseBranch?.Value,
        MaxActiveSpecsPerRepo = profile.MaxActiveSpecsPerRepo,
        SpecDependencyMode = profile.SpecDependencyMode,
        MaxConcurrentImplementersGlobal = profile.MaxConcurrentImplementersGlobal,
        MaxConcurrentImplementersPerRepo = profile.MaxConcurrentImplementersPerRepo,
        MaxReviewIterations = profile.MaxReviewIterations,
        MaxRetries = profile.MaxRetries,
        ParentReviewCycleLimit = profile.ParentReviewCycleLimit,
        TesterCycleLimit = profile.TesterCycleLimit,
        TroubleshooterEnabled = profile.TroubleshooterEnabled,
        TroubleshooterMaxAttempts = profile.TroubleshooterMaxAttempts,
        TesterRunInstructions = profile.TesterRunInstructions,
        TestPortRange = profile.TestPortRange is { } range ? new PortRangeData(range.Start, range.End) : null,
        Roles = profile.Roles.Where(entry => entry.Value != Unset).ToDictionary(),
    };

    public static EffectiveSettingsView ToView(EffectiveSettings settings) => new(
        settings.WorkspaceRootDirectory,
        settings.CopilotBaseDirectory,
        settings.BaseBranch.Value,
        settings.MaxActiveSpecsPerRepo,
        settings.SpecDependencyMode,
        settings.MaxConcurrentImplementersGlobal,
        settings.MaxConcurrentImplementersPerRepo,
        settings.MaxReviewIterations,
        settings.MaxRetries,
        settings.ParentReviewCycleLimit,
        settings.TesterCycleLimit,
        settings.TesterRunInstructions,
        new PortRangeData(settings.TestPortRange.Start, settings.TestPortRange.End),
        settings.Roles.ToDictionary(
            entry => entry.Key,
            entry => new EffectiveRoleSettingsView(entry.Value.Model, entry.Value.ReasoningEffort, entry.Value.PromptTemplate, (int)entry.Value.Timeout.TotalSeconds)),
        settings.TroubleshooterEnabled,
        settings.TroubleshooterMaxAttempts);

    // SettingsProfile has no role removal, so a role that is no longer overridden is reset to an empty (fall-through) override.
    private static void ApplyRoles(IReadOnlyDictionary<AgentRole, RoleSettingsOverride>? requested, SettingsProfile target)
    {
        foreach (AgentRole role in target.Roles.Keys.Where(role => requested?.ContainsKey(role) != true).ToArray())
        {
            target.SetRole(role, Unset);
        }

        foreach ((AgentRole role, RoleSettingsOverride settings) in requested ?? new Dictionary<AgentRole, RoleSettingsOverride>())
        {
            target.SetRole(role, settings);
        }
    }

    private static BranchName? ParseBranch(string? value, List<SettingsValidationError> errors)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return new BranchName(value);
        }
        catch (ArgumentException)
        {
            errors.Add(new SettingsValidationError(nameof(SettingsProfileData.BaseBranch), $"'{value}' is not a valid branch name."));
            return null;
        }
    }

    private static TestPortRange? ParsePortRange(PortRangeData? value, List<SettingsValidationError> errors)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return new TestPortRange(value.Start, value.End);
        }
        catch (ArgumentOutOfRangeException)
        {
            errors.Add(new SettingsValidationError(
                nameof(SettingsProfileData.TestPortRange),
                $"Port range {value.Start}-{value.End} must lie within {Domain.TestPortRange.MinPort}-{Domain.TestPortRange.MaxPort} with start <= end."));
            return null;
        }
    }
}
