using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Settings;
using WebDevLoop.Core.Tests.Ports.Fakes;
using WebDevLoop.Core.Tests.Settings;

namespace WebDevLoop.Core.Tests.Management;

public sealed class GlobalWorkspaceRootProviderTests
{
    private readonly InMemoryWorkflowStore _store = new();

    private GlobalWorkspaceRootProvider Provider => new(_store, new SettingsResolver(TestSettings.EmbeddedDefaults()));

    [Fact]
    public async Task without_a_global_profile_the_embedded_default_root_applies()
    {
        Assert.Equal("/defaults/workspaces", await Provider.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task the_global_override_wins_over_the_embedded_default()
    {
        SettingsProfile global = SettingsProfile.ForGlobal();
        global.WorkspaceRootDirectory = "/data/workspaces";
        _store.Add(global);

        Assert.Equal("/data/workspaces", await Provider.GetAsync(CancellationToken.None));
    }
}
