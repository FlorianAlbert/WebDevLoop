using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Management;
using WebDevLoop.Web.Components.Settings;

namespace WebDevLoop.Web.Tests.Components.Settings;

public sealed class SettingsEditModelTests
{
    [Fact]
    public void an_all_unset_layer_round_trips_to_all_null()
    {
        SettingsProfileData data = SettingsEditModel.FromData(new SettingsProfileData()).ToData();

        Assert.Equal(new SettingsProfileData(), data with { Roles = null });
        Assert.True(data.Roles is null or { Count: 0 });
    }

    [Fact]
    public void set_values_round_trip()
    {
        var original = new SettingsProfileData
        {
            BaseBranch = "develop",
            MaxRetries = 4,
            SpecDependencyMode = SpecDependencyMode.StackOnTop,
            TestPortRange = new PortRangeData(42000, 42100),
            TesterRunInstructions = "dotnet run",
            Roles = new Dictionary<AgentRole, RoleSettingsOverride>
            {
                [AgentRole.Tester] = new("gpt-x", "low", "Hello {repo_name}", 120),
            },
        };

        SettingsProfileData data = SettingsEditModel.FromData(original).ToData();

        Assert.Equal(original with { Roles = null }, data with { Roles = null });
        Assert.Equal(original.Roles![AgentRole.Tester], data.Roles![AgentRole.Tester]);
    }

    [Fact]
    public void blank_text_is_saved_as_null()
    {
        var model = SettingsEditModel.FromData(new SettingsProfileData());
        model.BaseBranch = "  ";
        model.TesterRunInstructions = "";
        model.Roles[AgentRole.Implementer].Model = " ";
        model.Roles[AgentRole.Implementer].PromptTemplate = "";

        SettingsProfileData data = model.ToData();

        Assert.Null(data.BaseBranch);
        Assert.Null(data.TesterRunInstructions);
        Assert.True(data.Roles is null || !data.Roles.ContainsKey(AgentRole.Implementer));
    }

    [Fact]
    public void troubleshooter_values_round_trip_and_zero_attempts_is_a_local_error()
    {
        var original = new SettingsProfileData { TroubleshooterEnabled = false, TroubleshooterMaxAttempts = 3 };

        var model = SettingsEditModel.FromData(original);
        SettingsProfileData data = model.ToData();

        Assert.Equal(original with { Roles = null }, data with { Roles = null });
        Assert.Empty(model.LocalErrors());

        model.TroubleshooterMaxAttempts = 0;

        Assert.Contains(model.LocalErrors(), error => error.Field == nameof(SettingsProfileData.TroubleshooterMaxAttempts));
    }

    [Fact]
    public void a_role_with_only_one_override_keeps_the_other_fields_null()
    {
        var model = SettingsEditModel.FromData(new SettingsProfileData());
        model.Roles[AgentRole.Explorer].TimeoutSeconds = 99;

        SettingsProfileData data = model.ToData();

        Assert.Equal(new RoleSettingsOverride(TimeoutSeconds: 99), data.Roles![AgentRole.Explorer]);
    }

    [Fact]
    public void a_half_filled_port_range_is_a_local_error_and_saved_as_null()
    {
        var model = SettingsEditModel.FromData(new SettingsProfileData());
        model.PortStart = 42000;

        Assert.Null(model.ToData().TestPortRange);
        Assert.Contains(model.LocalErrors(), error => error.Field == nameof(SettingsProfileData.TestPortRange));
    }

    [Fact]
    public void inherited_prompts_are_shown_and_collapse_back_to_null_unless_edited()
    {
        EffectiveSettingsView inherited = InheritedSettings.FromDefaults(SettingsTestData.Defaults);
        var model = SettingsEditModel.FromData(new SettingsProfileData
        {
            Roles = new Dictionary<AgentRole, RoleSettingsOverride> { [AgentRole.Tester] = new(PromptTemplate: "Custom {repo_name}") },
        });

        model.ShowInheritedPrompts(inherited);

        Assert.Equal("Custom {repo_name}", model.Roles[AgentRole.Tester].PromptTemplate);
        Assert.Equal(SettingsTestData.DefaultTemplate, model.Roles[AgentRole.Implementer].PromptTemplate);

        model.Roles[AgentRole.Tester].PromptTemplate = SettingsTestData.DefaultTemplate;
        model.CollapseInheritedPrompts(inherited);

        Assert.True(model.ToData().Roles is null or { Count: 0 });
    }
}
