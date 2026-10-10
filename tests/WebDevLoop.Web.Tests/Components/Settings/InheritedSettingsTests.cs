using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Management;
using WebDevLoop.Web.Components.Settings;

namespace WebDevLoop.Web.Tests.Components.Settings;

public sealed class InheritedSettingsTests
{
    [Fact]
    public void defaults_view_mirrors_the_embedded_defaults()
    {
        EffectiveSettingsView view = InheritedSettings.FromDefaults(SettingsTestData.Defaults);

        Assert.Equal(SettingsTestData.Defaults.MaxRetries, view.MaxRetries);
        Assert.Equal("main", view.BaseBranch);
        Assert.Equal(SettingsTestData.DefaultTemplate, view.Roles[AgentRole.Implementer].PromptTemplate);
        Assert.Equal(new PortRangeData(41000, 41999), view.TestPortRange);
    }

    [Fact]
    public void troubleshooter_values_are_inherited_from_global_then_defaults()
    {
        EffectiveSettingsView fromDefaults = InheritedSettings.FromDefaults(SettingsTestData.Defaults);
        EffectiveSettingsView underRepository = InheritedSettings.UnderRepository(
            new SettingsProfileData { TroubleshooterEnabled = false, TroubleshooterMaxAttempts = 5 }, SettingsTestData.Defaults);
        EffectiveSettingsView withoutGlobalValues = InheritedSettings.UnderRepository(new SettingsProfileData(), SettingsTestData.Defaults);

        Assert.Equal((true, 2), (fromDefaults.TroubleshooterEnabled, fromDefaults.TroubleshooterMaxAttempts));
        Assert.Equal((false, 5), (underRepository.TroubleshooterEnabled, underRepository.TroubleshooterMaxAttempts));
        Assert.Equal((true, 2), (withoutGlobalValues.TroubleshooterEnabled, withoutGlobalValues.TroubleshooterMaxAttempts));
        Assert.Contains(AgentRole.Troubleshooter, fromDefaults.Roles.Keys);
    }

    [Fact]
    public void repository_baseline_is_global_over_defaults()
    {
        var global = new SettingsProfileData
        {
            MaxRetries = 7,
            BaseBranch = " ",
            Roles = new Dictionary<AgentRole, RoleSettingsOverride> { [AgentRole.Tester] = new(Model: "gpt-global", TimeoutSeconds: 60) },
        };

        EffectiveSettingsView view = InheritedSettings.UnderRepository(global, SettingsTestData.Defaults);

        Assert.Equal(7, view.MaxRetries);
        Assert.Equal("main", view.BaseBranch);
        Assert.Equal("gpt-global", view.Roles[AgentRole.Tester].Model);
        Assert.Equal(60, view.Roles[AgentRole.Tester].TimeoutSeconds);
        Assert.Equal(SettingsTestData.Defaults.For(AgentRole.Tester).ReasoningEffort, view.Roles[AgentRole.Tester].ReasoningEffort);
        Assert.Equal(SettingsTestData.Defaults.MaxConcurrentImplementersPerRepo, view.MaxConcurrentImplementersPerRepo);
    }
}
