using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;
using WebDevLoop.Core.Tests.Ports.Fakes;
using WebDevLoop.Core.Tests.Settings;

namespace WebDevLoop.Core.Tests.Management;

public sealed class SettingsManagerTests
{
    private const int RepositoryId = 0;

    private readonly InMemoryWorkflowStore _store = new();
    private readonly SettingsManager _manager;

    public SettingsManagerTests()
    {
        var effective = new PersistedEffectiveSettingsProvider(_store, new SettingsResolver(TestSettings.EmbeddedDefaults()));
        _manager = new SettingsManager(_store, _store, effective, _store);
        ((IRepositoryRecordRepository)_store).Add(RepositoryRecord.Register(new GitHubRepoRef("acme", "widgets"), new BranchName("main"), "https://x/w.git", "/w", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public async Task global_settings_are_unset_until_first_saved()
    {
        SettingsProfileData data = await _manager.GetGlobalAsync(CancellationToken.None);

        Assert.Equal(new SettingsProfileData(), data);
    }

    [Fact]
    public async Task saving_global_settings_persists_them_and_round_trips_roles_and_prompt_templates()
    {
        var data = new SettingsProfileData
        {
            BaseBranch = "develop",
            MaxRetries = 4,
            SpecDependencyMode = SpecDependencyMode.StackOnTop,
            TestPortRange = new PortRangeData(42000, 42100),
            Roles = new Dictionary<AgentRole, RoleSettingsOverride>
            {
                [AgentRole.Tester] = new(Model: "m1", PromptTemplate: "Open {app_url} for {repo_name}.", TimeoutSeconds: 600),
            },
        };

        CommandResult<SettingsProfileData> saved = await _manager.SaveGlobalAsync(data, CancellationToken.None);
        SettingsProfileData loaded = await _manager.GetGlobalAsync(CancellationToken.None);

        Assert.Equal(CommandStatus.Succeeded, saved.Status);
        Assert.Equal(1, _store.SaveCount);
        Assert.Equal("develop", loaded.BaseBranch);
        Assert.Equal((4, SpecDependencyMode.StackOnTop, new PortRangeData(42000, 42100)), (loaded.MaxRetries, loaded.SpecDependencyMode, loaded.TestPortRange));
        Assert.Equal(data.Roles![AgentRole.Tester], loaded.Roles![AgentRole.Tester]);
    }

    [Fact]
    public async Task saving_global_settings_replaces_the_whole_layer()
    {
        await _manager.SaveGlobalAsync(new SettingsProfileData
        {
            MaxRetries = 4,
            MaxReviewIterations = 9,
            Roles = new Dictionary<AgentRole, RoleSettingsOverride> { [AgentRole.Explorer] = new(Model: "old") },
        }, CancellationToken.None);

        await _manager.SaveGlobalAsync(new SettingsProfileData { MaxRetries = 1 }, CancellationToken.None);
        SettingsProfileData loaded = await _manager.GetGlobalAsync(CancellationToken.None);

        Assert.Equal(new SettingsProfileData { MaxRetries = 1 }, loaded with { Roles = null });
        Assert.True(loaded.Roles is null or { Count: 0 });
        Assert.Equal(2, _store.SaveCount);
    }

    [Fact]
    public async Task invalid_global_settings_are_rejected_with_field_errors_and_not_saved()
    {
        var data = new SettingsProfileData
        {
            MaxRetries = -1,
            BaseBranch = "not a branch",
            TestPortRange = new PortRangeData(80, 8080),
            Roles = new Dictionary<AgentRole, RoleSettingsOverride> { [AgentRole.Implementer] = new(PromptTemplate: "Do {no_such_placeholder}") },
        };

        CommandResult<SettingsProfileData> result = await _manager.SaveGlobalAsync(data, CancellationToken.None);

        Assert.Equal(CommandStatus.Invalid, result.Status);
        string[] fields = result.Errors!.Select(error => error.Field).ToArray();
        Assert.Contains(nameof(SettingsProfileData.MaxRetries), fields);
        Assert.Contains(nameof(SettingsProfileData.BaseBranch), fields);
        Assert.Contains(nameof(SettingsProfileData.TestPortRange), fields);
        Assert.Contains("Roles.Implementer.PromptTemplate", fields);
        Assert.Equal(0, _store.SaveCount);
        Assert.Equal(new SettingsProfileData(), await _manager.GetGlobalAsync(CancellationToken.None));
    }

    [Fact]
    public async Task an_invalid_update_leaves_the_existing_layer_untouched()
    {
        await _manager.SaveGlobalAsync(new SettingsProfileData { MaxRetries = 4 }, CancellationToken.None);

        CommandResult<SettingsProfileData> result = await _manager.SaveGlobalAsync(new SettingsProfileData { MaxRetries = -5 }, CancellationToken.None);

        Assert.Equal(CommandStatus.Invalid, result.Status);
        Assert.Equal(4, (await _manager.GetGlobalAsync(CancellationToken.None)).MaxRetries);
    }

    [Fact]
    public async Task repository_overrides_can_be_saved_and_read_back()
    {
        var data = new SettingsProfileData
        {
            MaxActiveSpecsPerRepo = 2,
            Roles = new Dictionary<AgentRole, RoleSettingsOverride> { [AgentRole.ReviewerSpecification] = new(PromptTemplate: "Review {ticket_title}") },
        };

        CommandResult<SettingsProfileData> saved = await _manager.SaveRepositoryAsync(RepositoryId, data, CancellationToken.None);
        CommandResult<SettingsProfileData> loaded = await _manager.GetRepositoryAsync(RepositoryId, CancellationToken.None);

        Assert.Equal(CommandStatus.Succeeded, saved.Status);
        Assert.Equal(2, loaded.Value!.MaxActiveSpecsPerRepo);
        Assert.Equal("Review {ticket_title}", loaded.Value.Roles![AgentRole.ReviewerSpecification].PromptTemplate);
    }

    [Fact]
    public async Task repository_overrides_default_to_an_unset_layer()
    {
        CommandResult<SettingsProfileData> loaded = await _manager.GetRepositoryAsync(RepositoryId, CancellationToken.None);

        Assert.Equal(CommandStatus.Succeeded, loaded.Status);
        Assert.Equal(new SettingsProfileData(), loaded.Value);
    }

    [Fact]
    public async Task the_global_implementer_limit_cannot_be_overridden_per_repository()
    {
        CommandResult<SettingsProfileData> result = await _manager.SaveRepositoryAsync(
            RepositoryId, new SettingsProfileData { MaxConcurrentImplementersGlobal = 8 }, CancellationToken.None);

        Assert.Equal(CommandStatus.Invalid, result.Status);
        Assert.Contains(result.Errors!, error => error.Field == nameof(SettingsProfileData.MaxConcurrentImplementersGlobal));
    }

    [Fact]
    public async Task repository_settings_of_an_unknown_repository_are_not_found()
    {
        Assert.Equal(CommandStatus.NotFound, (await _manager.GetRepositoryAsync(42, CancellationToken.None)).Status);
        Assert.Equal(CommandStatus.NotFound, (await _manager.SaveRepositoryAsync(42, new SettingsProfileData(), CancellationToken.None)).Status);
        Assert.Equal(CommandStatus.NotFound, (await _manager.GetEffectiveAsync(42, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task effective_settings_resolve_repository_override_then_global_then_defaults()
    {
        await _manager.SaveGlobalAsync(new SettingsProfileData { MaxRetries = 7, MaxReviewIterations = 8 }, CancellationToken.None);
        await _manager.SaveRepositoryAsync(RepositoryId, new SettingsProfileData
        {
            MaxReviewIterations = 3,
            Roles = new Dictionary<AgentRole, RoleSettingsOverride> { [AgentRole.Tester] = new(PromptTemplate: "Open {app_url}", TimeoutSeconds: 90) },
        }, CancellationToken.None);

        CommandResult<EffectiveSettingsView> result = await _manager.GetEffectiveAsync(RepositoryId, CancellationToken.None);

        EffectiveSettingsView view = result.Value!;
        Assert.Equal((7, 3, "main", 2), (view.MaxRetries, view.MaxReviewIterations, view.BaseBranch, view.MaxConcurrentImplementersPerRepo));
        Assert.Equal(new EffectiveRoleSettingsView("default-model", "medium", "Open {app_url}", 90), view.Roles[AgentRole.Tester]);
        Assert.Equal(Enum.GetValues<AgentRole>().Length, view.Roles.Count);
    }

    [Fact]
    public async Task troubleshooter_settings_round_trip_and_resolve_into_the_effective_view()
    {
        await _manager.SaveGlobalAsync(new SettingsProfileData { TroubleshooterEnabled = false, TroubleshooterMaxAttempts = 5 }, CancellationToken.None);
        await _manager.SaveRepositoryAsync(RepositoryId, new SettingsProfileData { TroubleshooterEnabled = true }, CancellationToken.None);

        SettingsProfileData global = await _manager.GetGlobalAsync(CancellationToken.None);
        SettingsProfileData repository = (await _manager.GetRepositoryAsync(RepositoryId, CancellationToken.None)).Value!;
        EffectiveSettingsView view = (await _manager.GetEffectiveAsync(RepositoryId, CancellationToken.None)).Value!;

        Assert.Equal((false, 5), (global.TroubleshooterEnabled, global.TroubleshooterMaxAttempts));
        Assert.Equal((true, (int?)null), (repository.TroubleshooterEnabled, repository.TroubleshooterMaxAttempts));
        Assert.Equal((true, 5), (view.TroubleshooterEnabled, view.TroubleshooterMaxAttempts));
    }

    [Fact]
    public async Task a_global_profile_without_troubleshooter_values_resolves_to_the_defaults()
    {
        await _manager.SaveGlobalAsync(new SettingsProfileData { MaxRetries = 3 }, CancellationToken.None);

        EffectiveSettingsView view = (await _manager.GetEffectiveAsync(RepositoryId, CancellationToken.None)).Value!;

        Assert.Equal((true, 2), (view.TroubleshooterEnabled, view.TroubleshooterMaxAttempts));
        Assert.Contains(AgentRole.Troubleshooter, view.Roles.Keys);
    }

    [Fact]
    public async Task zero_troubleshooter_attempts_are_rejected()
    {
        CommandResult<SettingsProfileData> result = await _manager.SaveGlobalAsync(new SettingsProfileData { TroubleshooterMaxAttempts = 0 }, CancellationToken.None);

        Assert.Equal(CommandStatus.Invalid, result.Status);
        Assert.Contains(result.Errors!, error => error.Field == nameof(SettingsProfileData.TroubleshooterMaxAttempts));
    }

    [Fact]
    public async Task effective_settings_conflict_when_global_settings_were_never_seeded()
    {
        CommandResult<EffectiveSettingsView> result = await _manager.GetEffectiveAsync(RepositoryId, CancellationToken.None);

        Assert.Equal(CommandStatus.Conflict, result.Status);
    }
}
