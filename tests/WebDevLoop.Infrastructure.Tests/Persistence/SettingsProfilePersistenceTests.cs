using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Infrastructure.Tests.Persistence;

public sealed class SettingsProfilePersistenceTests : IDisposable
{
    private readonly PersistenceHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task global_profile_round_trips_every_value_and_role_override()
    {
        using (PersistenceScope write = _harness.OpenScope())
        {
            SettingsProfile global = SettingsProfile.ForGlobal();
            global.WorkspaceRootDirectory = "/work";
            global.CopilotBaseDirectory = "/copilot";
            global.BaseBranch = new BranchName("trunk");
            global.MaxActiveSpecsPerRepo = 2;
            global.SpecDependencyMode = SpecDependencyMode.StackOnTop;
            global.MaxConcurrentImplementersGlobal = 4;
            global.MaxConcurrentImplementersPerRepo = 2;
            global.MaxReviewIterations = 3;
            global.MaxRetries = 1;
            global.ParentReviewCycleLimit = 5;
            global.TesterCycleLimit = 6;
            global.TesterRunInstructions = "dotnet run";
            global.TestPortRange = new TestPortRange(5000, 5100);
            global.PatFallbackEnabled = false;
            global.SetRole(AgentRole.Implementer, new RoleSettingsOverride("gpt-x", "high", "Do {ticket_title}", 900));
            global.SetRole(AgentRole.Tester, new RoleSettingsOverride(Model: "m2"));
            write.Settings.Add(global);
            await write.SaveAsync();
        }

        using PersistenceScope read = _harness.OpenScope();
        SettingsProfile loaded = (await read.Settings.GetGlobalAsync(CancellationToken.None))!;

        Assert.True(loaded.IsGlobal);
        Assert.Equal("/work", loaded.WorkspaceRootDirectory);
        Assert.Equal("/copilot", loaded.CopilotBaseDirectory);
        Assert.Equal(new BranchName("trunk"), loaded.BaseBranch);
        Assert.Equal(2, loaded.MaxActiveSpecsPerRepo);
        Assert.Equal(SpecDependencyMode.StackOnTop, loaded.SpecDependencyMode);
        Assert.Equal(4, loaded.MaxConcurrentImplementersGlobal);
        Assert.Equal(2, loaded.MaxConcurrentImplementersPerRepo);
        Assert.Equal(3, loaded.MaxReviewIterations);
        Assert.Equal(1, loaded.MaxRetries);
        Assert.Equal(5, loaded.ParentReviewCycleLimit);
        Assert.Equal(6, loaded.TesterCycleLimit);
        Assert.Equal("dotnet run", loaded.TesterRunInstructions);
        Assert.Equal(new TestPortRange(5000, 5100), loaded.TestPortRange);
        Assert.False(loaded.PatFallbackEnabled);
        Assert.Equal(new RoleSettingsOverride("gpt-x", "high", "Do {ticket_title}", 900), loaded.Roles[AgentRole.Implementer]);
        Assert.Equal(new RoleSettingsOverride(Model: "m2"), loaded.Roles[AgentRole.Tester]);
        Assert.Equal(2, loaded.Roles.Count);
    }

    [Fact]
    public async Task repository_override_keeps_unset_values_null()
    {
        (int repositoryId, RunId _) = await TestData.SeedSpecRunAsync(_harness);
        using (PersistenceScope write = _harness.OpenScope())
        {
            write.Settings.Add(SettingsProfile.ForGlobal());
            SettingsProfile overrides = SettingsProfile.ForRepository(repositoryId);
            overrides.MaxRetries = 7;
            overrides.TesterRunInstructions = "npm start";
            overrides.SetRole(AgentRole.Explorer, new RoleSettingsOverride(TimeoutSeconds: 60));
            write.Settings.Add(overrides);
            await write.SaveAsync();
        }

        using PersistenceScope read = _harness.OpenScope();
        SettingsProfile loaded = (await read.Settings.FindForRepositoryAsync(repositoryId, CancellationToken.None))!;

        Assert.False(loaded.IsGlobal);
        Assert.Equal(repositoryId, loaded.RepositoryId);
        Assert.Equal(7, loaded.MaxRetries);
        Assert.Equal("npm start", loaded.TesterRunInstructions);
        Assert.Null(loaded.WorkspaceRootDirectory);
        Assert.Null(loaded.BaseBranch);
        Assert.Null(loaded.MaxActiveSpecsPerRepo);
        Assert.Null(loaded.SpecDependencyMode);
        Assert.Null(loaded.MaxReviewIterations);
        Assert.Null(loaded.TestPortRange);
        Assert.Null(loaded.PatFallbackEnabled);
        Assert.Equal(new RoleSettingsOverride(TimeoutSeconds: 60), loaded.Roles[AgentRole.Explorer]);
        Assert.Null((await read.Settings.GetGlobalAsync(CancellationToken.None))!.MaxRetries);
    }

    [Fact]
    public async Task changing_a_role_override_and_clearing_a_value_on_a_loaded_profile_is_saved()
    {
        using (PersistenceScope write = _harness.OpenScope())
        {
            SettingsProfile global = SettingsProfile.ForGlobal();
            global.MaxRetries = 3;
            global.TestPortRange = new TestPortRange(5000, 5100);
            global.SetRole(AgentRole.Implementer, new RoleSettingsOverride(Model: "old"));
            write.Settings.Add(global);
            await write.SaveAsync();
        }

        using (PersistenceScope update = _harness.OpenScope())
        {
            SettingsProfile global = (await update.Settings.GetGlobalAsync(CancellationToken.None))!;
            global.SetRole(AgentRole.Implementer, new RoleSettingsOverride(Model: "new"));
            global.SetRole(AgentRole.Tester, new RoleSettingsOverride(Model: "added"));
            global.MaxRetries = null;
            global.TestPortRange = null;
            await update.SaveAsync();
        }

        using PersistenceScope read = _harness.OpenScope();
        SettingsProfile loaded = (await read.Settings.GetGlobalAsync(CancellationToken.None))!;

        Assert.Equal("new", loaded.Roles[AgentRole.Implementer].Model);
        Assert.Equal("added", loaded.Roles[AgentRole.Tester].Model);
        Assert.Null(loaded.MaxRetries);
        Assert.Null(loaded.TestPortRange);
    }
}
