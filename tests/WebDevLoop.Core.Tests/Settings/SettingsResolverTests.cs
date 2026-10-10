using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Tests.Settings;

public sealed class SettingsResolverTests
{
    private const int RepositoryId = 7;

    private readonly SettingsResolver _resolver = new(TestSettings.EmbeddedDefaults());

    [Fact]
    public void repo_stack_on_top_overrides_global_wait_for_merge()
    {
        SettingsProfile global = SettingsProfile.ForGlobal();
        global.SpecDependencyMode = SpecDependencyMode.WaitForMerge;
        SettingsProfile repository = SettingsProfile.ForRepository(RepositoryId);
        repository.SpecDependencyMode = SpecDependencyMode.StackOnTop;

        EffectiveSettings effective = _resolver.Resolve(global, repository);

        Assert.Equal(SpecDependencyMode.StackOnTop, effective.SpecDependencyMode);
    }

    [Fact]
    public void missing_repo_role_timeout_falls_back_to_global_timeout()
    {
        SettingsProfile global = SettingsProfile.ForGlobal();
        global.SetRole(AgentRole.Implementer, new RoleSettingsOverride(TimeoutSeconds: 5400));
        SettingsProfile repository = SettingsProfile.ForRepository(RepositoryId);
        repository.SetRole(AgentRole.Implementer, new RoleSettingsOverride(Model: "repo-model"));

        RoleSettings implementer = _resolver.Resolve(global, repository).For(AgentRole.Implementer);

        Assert.Equal(TimeSpan.FromSeconds(5400), implementer.Timeout);
        Assert.Equal("repo-model", implementer.Model);
    }

    [Fact]
    public void missing_repo_and_global_role_timeout_falls_back_to_embedded_default()
    {
        SettingsProfile repository = SettingsProfile.ForRepository(RepositoryId);
        repository.SetRole(AgentRole.Tester, new RoleSettingsOverride(ReasoningEffort: "high"));

        RoleSettings tester = _resolver.Resolve(SettingsProfile.ForGlobal(), repository).For(AgentRole.Tester);

        Assert.Equal(TestSettings.DefaultTimeout, tester.Timeout);
        Assert.Equal("high", tester.ReasoningEffort);
    }

    [Fact]
    public void blank_repo_text_inherits_global_value()
    {
        SettingsProfile global = SettingsProfile.ForGlobal();
        global.TesterRunInstructions = "npm start";
        SettingsProfile repository = SettingsProfile.ForRepository(RepositoryId);
        repository.TesterRunInstructions = "  ";

        EffectiveSettings effective = _resolver.Resolve(global, repository);

        Assert.Equal("npm start", effective.TesterRunInstructions);
    }

    [Fact]
    public void repository_profile_cannot_change_global_implementer_limit()
    {
        SettingsProfile global = SettingsProfile.ForGlobal();
        global.MaxConcurrentImplementersGlobal = 6;
        SettingsProfile repository = SettingsProfile.ForRepository(RepositoryId);
        repository.MaxConcurrentImplementersGlobal = 20;

        EffectiveSettings effective = _resolver.Resolve(global, repository);

        Assert.Equal(6, effective.MaxConcurrentImplementersGlobal);
    }

    [Fact]
    public void troubleshooter_settings_fall_back_to_embedded_defaults_when_no_layer_sets_them()
    {
        EffectiveSettings effective = _resolver.Resolve(SettingsProfile.ForGlobal(), SettingsProfile.ForRepository(RepositoryId));

        Assert.True(effective.TroubleshooterEnabled);
        Assert.Equal(2, effective.TroubleshooterMaxAttempts);
    }

    [Fact]
    public void global_troubleshooter_settings_apply_and_repository_overrides_win()
    {
        SettingsProfile global = SettingsProfile.ForGlobal();
        global.TroubleshooterEnabled = false;
        global.TroubleshooterMaxAttempts = 4;
        SettingsProfile repository = SettingsProfile.ForRepository(RepositoryId);

        EffectiveSettings inherited = _resolver.Resolve(global, repository);
        repository.TroubleshooterEnabled = true;
        repository.TroubleshooterMaxAttempts = 1;
        EffectiveSettings overridden = _resolver.Resolve(global, repository);

        Assert.False(inherited.TroubleshooterEnabled);
        Assert.Equal(4, inherited.TroubleshooterMaxAttempts);
        Assert.True(overridden.TroubleshooterEnabled);
        Assert.Equal(1, overridden.TroubleshooterMaxAttempts);
    }

    [Fact]
    public void repository_can_turn_the_troubleshooter_off_while_global_leaves_it_on()
    {
        SettingsProfile global = SettingsProfile.ForGlobal();
        global.TroubleshooterEnabled = true;
        SettingsProfile repository = SettingsProfile.ForRepository(RepositoryId);
        repository.TroubleshooterEnabled = false;

        Assert.False(_resolver.Resolve(global, repository).TroubleshooterEnabled);
    }

    [Fact]
    public void troubleshooter_role_inherits_repository_then_global_then_embedded_default()
    {
        SettingsProfile global = SettingsProfile.ForGlobal();
        global.SetRole(AgentRole.Troubleshooter, new RoleSettingsOverride(Model: "global-model", TimeoutSeconds: 900));
        SettingsProfile repository = SettingsProfile.ForRepository(RepositoryId);
        repository.SetRole(AgentRole.Troubleshooter, new RoleSettingsOverride(Model: "repo-model"));

        RoleSettings role = _resolver.Resolve(global, repository).For(AgentRole.Troubleshooter);

        Assert.Equal("repo-model", role.Model);
        Assert.Equal(TimeSpan.FromSeconds(900), role.Timeout);
        Assert.Equal("medium", role.ReasoningEffort);
        Assert.Equal("default Troubleshooter template", role.PromptTemplate);
    }

    [Fact]
    public void swapped_profiles_are_rejected()
    {
        Assert.Throws<ArgumentException>(
            () => _resolver.Resolve(SettingsProfile.ForRepository(RepositoryId), SettingsProfile.ForGlobal()));
    }
}
