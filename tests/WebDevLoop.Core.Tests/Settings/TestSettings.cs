using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Tests.Settings;

internal static class TestSettings
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(30);

    public static EffectiveSettings EmbeddedDefaults() => new()
    {
        WorkspaceRootDirectory = "/defaults/workspaces",
        CopilotBaseDirectory = "/defaults/copilot",
        BaseBranch = new BranchName("main"),
        MaxActiveSpecsPerRepo = 1,
        SpecDependencyMode = SpecDependencyMode.WaitForMerge,
        MaxConcurrentImplementersGlobal = 4,
        MaxConcurrentImplementersPerRepo = 2,
        MaxReviewIterations = 5,
        MaxRetries = 2,
        ParentReviewCycleLimit = 3,
        TesterCycleLimit = 3,
        TesterRunInstructions = "default run instructions",
        TestPortRange = new TestPortRange(41000, 41999),
        PatFallbackEnabled = true,
        Roles = Enum.GetValues<AgentRole>().ToDictionary(
            role => role,
            role => new RoleSettings("default-model", "medium", $"default {role} template", DefaultTimeout)),
    };
}
