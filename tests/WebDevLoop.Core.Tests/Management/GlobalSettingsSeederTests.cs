using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;
using WebDevLoop.Core.Tests.Ports.Fakes;
using WebDevLoop.Core.Tests.Settings;

namespace WebDevLoop.Core.Tests.Management;

public sealed class GlobalSettingsSeederTests
{
    private readonly InMemoryWorkflowStore _store = new();
    private readonly EffectiveSettings _defaults = TestSettings.EmbeddedDefaults() with
    {
        Roles = Enum.GetValues<AgentRole>().ToDictionary(role => role, role => new RoleSettings($"{role}-model", "high", $"{role} prompt", TimeSpan.FromMinutes(7))),
    };

    private GlobalSettingsSeeder Seeder => new(_store, _store, _defaults);

    [Fact]
    public async Task first_start_copies_every_embedded_default_including_role_prompts_into_global_settings()
    {
        bool seeded = await Seeder.SeedAsync(TestContext.Current.CancellationToken);

        SettingsProfile global = Assert.IsType<SettingsProfile>(await ((ISettingsProfileRepository)_store).GetGlobalAsync(TestContext.Current.CancellationToken));
        Assert.True(seeded);
        Assert.Equal(1, _store.SaveCount);
        Assert.Equal(_defaults.WorkspaceRootDirectory, global.WorkspaceRootDirectory);
        Assert.Equal(_defaults.CopilotBaseDirectory, global.CopilotBaseDirectory);
        Assert.Equal(_defaults.BaseBranch, global.BaseBranch);
        Assert.Equal(_defaults.MaxActiveSpecsPerRepo, global.MaxActiveSpecsPerRepo);
        Assert.Equal(_defaults.SpecDependencyMode, global.SpecDependencyMode);
        Assert.Equal(_defaults.MaxConcurrentImplementersGlobal, global.MaxConcurrentImplementersGlobal);
        Assert.Equal(_defaults.MaxConcurrentImplementersPerRepo, global.MaxConcurrentImplementersPerRepo);
        Assert.Equal(_defaults.MaxReviewIterations, global.MaxReviewIterations);
        Assert.Equal(_defaults.MaxRetries, global.MaxRetries);
        Assert.Equal(_defaults.ParentReviewCycleLimit, global.ParentReviewCycleLimit);
        Assert.Equal(_defaults.TesterCycleLimit, global.TesterCycleLimit);
        Assert.Equal(_defaults.TesterRunInstructions, global.TesterRunInstructions);
        Assert.Equal(_defaults.TestPortRange, global.TestPortRange);
        Assert.All(Enum.GetValues<AgentRole>(), role => Assert.Equal(
            new RoleSettingsOverride($"{role}-model", "high", $"{role} prompt", 420),
            global.Roles[role]));
    }

    [Fact]
    public async Task an_existing_global_profile_is_left_untouched()
    {
        SettingsProfile existing = SettingsProfile.ForGlobal();
        existing.MaxRetries = 9;
        _store.Add(existing);

        bool seeded = await Seeder.SeedAsync(TestContext.Current.CancellationToken);

        Assert.False(seeded);
        Assert.Equal(0, _store.SaveCount);
        Assert.Equal(9, existing.MaxRetries);
        Assert.Null(existing.WorkspaceRootDirectory);
    }
}
