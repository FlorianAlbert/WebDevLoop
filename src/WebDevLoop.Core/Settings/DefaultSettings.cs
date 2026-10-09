using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Settings;

/// <summary>Builds the embedded-default settings layer, the last fallback of settings resolution.</summary>
public static class DefaultSettings
{
    public static BranchName BaseBranch { get; } = new("main");
    public const int MaxActiveSpecsPerRepo = 1;
    public const SpecDependencyMode DependencyMode = SpecDependencyMode.WaitForMerge;
    public const int MaxConcurrentImplementersGlobal = 4;
    public const int MaxConcurrentImplementersPerRepo = 2;
    public const int MaxReviewIterations = 5;
    public const int MaxRetries = 2;
    public const int ParentReviewCycleLimit = 3;
    public const int TesterCycleLimit = 3;
    public const bool PatFallbackEnabled = true;
    public const string WorkspacesDirectoryName = "workspaces";
    public const string CopilotDirectoryName = "copilot";

    public const string TesterRunInstructions =
        "No run instructions are configured for this repository. Discover how to build and start the application "
        + "from the repository documentation (README, CONTRIBUTING, launch settings, package scripts), and make it "
        + "listen on the reserved port.";

    public static TestPortRange TestPortRange { get; } = new(41000, 41999);

    private const string StandardModel = "claude-sonnet-5";
    private const string ReviewModel = "claude-opus-5";
    private const string MediumEffort = "medium";
    private const string HighEffort = "high";

    private static readonly IReadOnlyDictionary<AgentRole, (string Model, string ReasoningEffort, TimeSpan Timeout)> RoleDefaults =
        new Dictionary<AgentRole, (string, string, TimeSpan)>
        {
            [AgentRole.Explorer] = (StandardModel, MediumEffort, TimeSpan.FromMinutes(30)),
            [AgentRole.Implementer] = (StandardModel, HighEffort, TimeSpan.FromMinutes(90)),
            [AgentRole.ReviewerCodingStandards] = (ReviewModel, HighEffort, TimeSpan.FromMinutes(30)),
            [AgentRole.ReviewerSpecification] = (ReviewModel, HighEffort, TimeSpan.FromMinutes(30)),
            [AgentRole.ConflictResolver] = (StandardModel, HighEffort, TimeSpan.FromMinutes(45)),
            [AgentRole.Tester] = (StandardModel, MediumEffort, TimeSpan.FromMinutes(60)),
        };

    /// <exception cref="SettingsValidationException">A shipped template is invalid (fail fast at startup).</exception>
    public static EffectiveSettings Create(IDefaultPromptTemplates templates, string dataRoot)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        Dictionary<AgentRole, RoleSettings> roles = Enum.GetValues<AgentRole>().ToDictionary(
            role => role,
            role => CreateRole(role, templates.GetTemplate(role)));

        List<SettingsValidationError> errors = roles
            .SelectMany(entry => PromptTemplateValidator.Validate(entry.Key, entry.Value.PromptTemplate))
            .ToList();
        if (errors.Count > 0)
        {
            throw new SettingsValidationException(errors);
        }

        return new EffectiveSettings
        {
            WorkspaceRootDirectory = Path.Combine(dataRoot, WorkspacesDirectoryName),
            CopilotBaseDirectory = Path.Combine(dataRoot, CopilotDirectoryName),
            BaseBranch = BaseBranch,
            MaxActiveSpecsPerRepo = MaxActiveSpecsPerRepo,
            SpecDependencyMode = DependencyMode,
            MaxConcurrentImplementersGlobal = MaxConcurrentImplementersGlobal,
            MaxConcurrentImplementersPerRepo = MaxConcurrentImplementersPerRepo,
            MaxReviewIterations = MaxReviewIterations,
            MaxRetries = MaxRetries,
            ParentReviewCycleLimit = ParentReviewCycleLimit,
            TesterCycleLimit = TesterCycleLimit,
            TesterRunInstructions = TesterRunInstructions,
            TestPortRange = TestPortRange,
            PatFallbackEnabled = PatFallbackEnabled,
            Roles = roles,
        };
    }

    private static RoleSettings CreateRole(AgentRole role, string template)
    {
        (string model, string reasoningEffort, TimeSpan timeout) = RoleDefaults[role];
        return new RoleSettings(model, reasoningEffort, template, timeout);
    }
}
