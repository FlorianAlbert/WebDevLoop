using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Web.Components.Settings;

/// <summary>
/// Mutable form state of one settings layer. <c>null</c> (or blank text) means "not overridden here" and is persisted as <c>null</c>,
/// so an untouched field keeps inheriting.
/// </summary>
public sealed class SettingsEditModel
{
    public string? WorkspaceRootDirectory { get; set; }

    public string? CopilotBaseDirectory { get; set; }

    public string? BaseBranch { get; set; }

    public int? MaxActiveSpecsPerRepo { get; set; }

    public SpecDependencyMode? SpecDependencyMode { get; set; }

    public int? MaxConcurrentImplementersGlobal { get; set; }

    public int? MaxConcurrentImplementersPerRepo { get; set; }

    public int? MaxReviewIterations { get; set; }

    public int? MaxRetries { get; set; }

    public int? ParentReviewCycleLimit { get; set; }

    public int? TesterCycleLimit { get; set; }

    public string? TesterRunInstructions { get; set; }

    public int? PortStart { get; set; }

    public int? PortEnd { get; set; }

    public Dictionary<AgentRole, RoleEditModel> Roles { get; } =
        Enum.GetValues<AgentRole>().ToDictionary(role => role, _ => new RoleEditModel());

    public static SettingsEditModel FromData(SettingsProfileData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var model = new SettingsEditModel
        {
            WorkspaceRootDirectory = data.WorkspaceRootDirectory,
            CopilotBaseDirectory = data.CopilotBaseDirectory,
            BaseBranch = data.BaseBranch,
            MaxActiveSpecsPerRepo = data.MaxActiveSpecsPerRepo,
            SpecDependencyMode = data.SpecDependencyMode,
            MaxConcurrentImplementersGlobal = data.MaxConcurrentImplementersGlobal,
            MaxConcurrentImplementersPerRepo = data.MaxConcurrentImplementersPerRepo,
            MaxReviewIterations = data.MaxReviewIterations,
            MaxRetries = data.MaxRetries,
            ParentReviewCycleLimit = data.ParentReviewCycleLimit,
            TesterCycleLimit = data.TesterCycleLimit,
            TesterRunInstructions = data.TesterRunInstructions,
            PortStart = data.TestPortRange?.Start,
            PortEnd = data.TestPortRange?.End,
        };

        foreach ((AgentRole role, RoleSettingsOverride settings) in data.Roles ?? new Dictionary<AgentRole, RoleSettingsOverride>())
        {
            model.Roles[role] = RoleEditModel.FromOverride(settings);
        }

        return model;
    }

    /// <summary>Repository layers cannot override the startup-scoped directories; a value saved by an older version is dropped on the next save.</summary>
    public void DiscardStartupScopedDirectories()
    {
        WorkspaceRootDirectory = null;
        CopilotBaseDirectory = null;
    }

    public SettingsProfileData ToData() => new()
    {
        WorkspaceRootDirectory = NullIfBlank(WorkspaceRootDirectory),
        CopilotBaseDirectory = NullIfBlank(CopilotBaseDirectory),
        BaseBranch = NullIfBlank(BaseBranch),
        MaxActiveSpecsPerRepo = MaxActiveSpecsPerRepo,
        SpecDependencyMode = SpecDependencyMode,
        MaxConcurrentImplementersGlobal = MaxConcurrentImplementersGlobal,
        MaxConcurrentImplementersPerRepo = MaxConcurrentImplementersPerRepo,
        MaxReviewIterations = MaxReviewIterations,
        MaxRetries = MaxRetries,
        ParentReviewCycleLimit = ParentReviewCycleLimit,
        TesterCycleLimit = TesterCycleLimit,
        TesterRunInstructions = NullIfBlank(TesterRunInstructions),
        TestPortRange = PortStart is { } start && PortEnd is { } end ? new PortRangeData(start, end) : null,
        Roles = Roles
            .Select(entry => (entry.Key, Override: entry.Value.ToOverride()))
            .Where(entry => entry.Override != new RoleSettingsOverride())
            .ToDictionary(entry => entry.Key, entry => entry.Override),
    };

    /// <summary>Problems only the form can detect, because <see cref="ToData"/> cannot express them.</summary>
    public IReadOnlyList<SettingsValidationError> LocalErrors() =>
        PortStart.HasValue != PortEnd.HasValue
            ? [new SettingsValidationError(nameof(SettingsProfileData.TestPortRange), "Set both the first and the last port, or neither.")]
            : [];

    /// <summary>The data to persist: a copy with prompts that still equal the inherited template collapsed to "not overridden".</summary>
    public SettingsProfileData ToPersistedData(EffectiveSettingsView inherited)
    {
        SettingsEditModel copy = FromData(ToData());
        copy.CollapseInheritedPrompts(inherited);
        return copy.ToData();
    }

    /// <summary>Pre-fills every role prompt that is not overridden with the inherited template, so the editor always shows the text in effect.</summary>
    public void ShowInheritedPrompts(EffectiveSettingsView inherited)
    {
        foreach ((AgentRole role, RoleEditModel edit) in Roles)
        {
            edit.PromptTemplate = NullIfBlank(edit.PromptTemplate) ?? inherited.Roles[role].PromptTemplate;
        }
    }

    /// <summary>Turns prompts still equal to the inherited template back into "not overridden", so they keep following the inherited value.</summary>
    public void CollapseInheritedPrompts(EffectiveSettingsView inherited)
    {
        foreach ((AgentRole role, RoleEditModel edit) in Roles)
        {
            if (edit.PromptTemplate == inherited.Roles[role].PromptTemplate)
            {
                edit.PromptTemplate = null;
            }
        }
    }

    internal static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

public sealed class RoleEditModel
{
    public string? Model { get; set; }

    public string? ReasoningEffort { get; set; }

    public string? PromptTemplate { get; set; }

    public int? TimeoutSeconds { get; set; }

    public static RoleEditModel FromOverride(RoleSettingsOverride settings) => new()
    {
        Model = settings.Model,
        ReasoningEffort = settings.ReasoningEffort,
        PromptTemplate = settings.PromptTemplate,
        TimeoutSeconds = settings.TimeoutSeconds,
    };

    public RoleSettingsOverride ToOverride() => new(
        SettingsEditModel.NullIfBlank(Model),
        SettingsEditModel.NullIfBlank(ReasoningEffort),
        SettingsEditModel.NullIfBlank(PromptTemplate),
        TimeoutSeconds);
}
