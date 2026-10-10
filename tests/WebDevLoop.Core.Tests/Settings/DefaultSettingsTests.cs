using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Tests.Settings;

public sealed class DefaultSettingsTests
{
    private const string DataRoot = "/var/lib/webdevloop";

    [Fact]
    public void defaults_are_sequential_wait_for_merge_with_paths_under_data_root()
    {
        EffectiveSettings defaults = DefaultSettings.Create(new FakeTemplates(), DataRoot);

        Assert.Equal(1, defaults.MaxActiveSpecsPerRepo);
        Assert.Equal(SpecDependencyMode.WaitForMerge, defaults.SpecDependencyMode);
        Assert.Equal(Path.Combine(DataRoot, "workspaces"), defaults.WorkspaceRootDirectory);
        Assert.Equal(Path.Combine(DataRoot, "copilot"), defaults.CopilotBaseDirectory);
        Assert.Equal(new BranchName("main"), defaults.BaseBranch);
        Assert.True(defaults.TestPortRange.Start >= SettingsValidator.MinimumUnprivilegedPort);
    }

    [Fact]
    public void every_role_gets_its_shipped_template_and_a_positive_timeout()
    {
        EffectiveSettings defaults = DefaultSettings.Create(new FakeTemplates(), DataRoot);

        Assert.All(Enum.GetValues<AgentRole>(), role =>
        {
            RoleSettings settings = defaults.For(role);
            Assert.Equal($"{role} works in {{worktree_path}}.", settings.PromptTemplate);
            Assert.True(settings.Timeout > TimeSpan.Zero);
            Assert.False(string.IsNullOrWhiteSpace(settings.Model));
        });
    }

    [Fact]
    public void troubleshooter_is_enabled_with_two_attempts_and_a_high_effort_role()
    {
        EffectiveSettings defaults = DefaultSettings.Create(new FakeTemplates(), DataRoot);

        Assert.True(defaults.TroubleshooterEnabled);
        Assert.Equal(2, defaults.TroubleshooterMaxAttempts);
        RoleSettings troubleshooter = defaults.For(AgentRole.Troubleshooter);
        Assert.False(string.IsNullOrWhiteSpace(troubleshooter.Model));
        Assert.Equal("high", troubleshooter.ReasoningEffort);
        Assert.Equal(TimeSpan.FromMinutes(30), troubleshooter.Timeout);
    }

    [Fact]
    public void invalid_shipped_template_fails_fast()
    {
        var templates = new FakeTemplates { [AgentRole.Tester] = "Open {unknown_thing}." };

        var error = Assert.Throws<SettingsValidationException>(() => DefaultSettings.Create(templates, DataRoot));

        Assert.Equal("Roles.Tester.PromptTemplate", Assert.Single(error.Errors).Field);
    }

    private sealed class FakeTemplates : Dictionary<AgentRole, string>, IDefaultPromptTemplates
    {
        public string GetTemplate(AgentRole role) =>
            TryGetValue(role, out string? template) ? template : $"{role} works in {{worktree_path}}.";
    }
}
