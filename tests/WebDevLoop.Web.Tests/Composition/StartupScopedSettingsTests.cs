using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;
using WebDevLoop.Web.DependencyInjection;
using WebDevLoop.Web.Tests.Workflow;

namespace WebDevLoop.Web.Tests.Composition;

/// <summary>The workspace root and the Copilot home configure process-wide resources, so they are global and fixed until a restart.</summary>
public sealed class StartupScopedSettingsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task the_effective_settings_keep_the_startup_directories_when_the_global_values_change_at_runtime()
    {
        using var scenario = new WorkflowScenario();
        await using var host = new WorkflowHost(scenario);
        var api = new WorkflowApi(host.CreateClient(), scenario.Logs);
        int repositoryId = await api.RegisterRepositoryAsync(scenario);
        EffectiveSettings started = host.Services.GetRequiredService<StartupSettings>().Current;

        await ChangeGlobalDirectoriesAsync(host, scenario);

        EffectiveSettings effective = await EffectiveAsync(host, repositoryId);
        Assert.Equal((started.WorkspaceRootDirectory, started.CopilotBaseDirectory), (effective.WorkspaceRootDirectory, effective.CopilotBaseDirectory));
    }

    [Fact]
    public async Task a_persisted_per_repository_directory_override_never_reaches_the_runners()
    {
        using var scenario = new WorkflowScenario();
        await using var host = new WorkflowHost(scenario);
        var api = new WorkflowApi(host.CreateClient(), scenario.Logs);
        int repositoryId = await api.RegisterRepositoryAsync(scenario);
        EffectiveSettings started = host.Services.GetRequiredService<StartupSettings>().Current;
        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
        {
            SettingsProfile legacy = SettingsProfile.ForRepository(repositoryId);
            legacy.WorkspaceRootDirectory = Path.Combine(scenario.Sandbox.Root, "legacy-override");
            legacy.CopilotBaseDirectory = Path.Combine(scenario.Sandbox.Root, "legacy-copilot");
            scope.ServiceProvider.GetRequiredService<ISettingsProfileRepository>().Add(legacy);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);
        }

        EffectiveSettings effective = await EffectiveAsync(host, repositoryId);

        Assert.Equal((started.WorkspaceRootDirectory, started.CopilotBaseDirectory), (effective.WorkspaceRootDirectory, effective.CopilotBaseDirectory));
    }

    [Fact]
    public async Task the_settings_api_rejects_a_per_repository_workspace_root()
    {
        using var scenario = new WorkflowScenario();
        await using var host = new WorkflowHost(scenario);
        HttpClient client = host.CreateClient();
        int repositoryId = await new WorkflowApi(client, scenario.Logs).RegisterRepositoryAsync(scenario);

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/repos/{repositoryId}/settings", new { workspaceRootDirectory = Path.Combine(scenario.Sandbox.Root, "elsewhere") }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("WorkspaceRootDirectory", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task a_changed_global_workspace_root_applies_after_a_restart_and_warns_about_clones_under_the_old_root()
    {
        using var scenario = new WorkflowScenario();
        string newRoot = Path.Combine(scenario.Sandbox.Root, "moved-workspaces");
        string oldClone;
        await using (var first = new WorkflowHost(scenario))
        {
            int repositoryId = await new WorkflowApi(first.CreateClient(), scenario.Logs).RegisterRepositoryAsync(scenario);
            oldClone = (await first.CreateClient().GetFromJsonAsync<JsonElement>($"/api/repos/{repositoryId}", Ct)).GetProperty("localPath").GetString()!;
            await ChangeGlobalDirectoriesAsync(first, scenario, newRoot);
            Assert.DoesNotContain(scenario.Logs.Entries, entry => entry.Contains(oldClone, StringComparison.Ordinal));
        }

        await using var second = new WorkflowHost(scenario);
        _ = second.CreateClient();

        Assert.Equal(newRoot, second.Services.GetRequiredService<StartupSettings>().Current.WorkspaceRootDirectory);
        Assert.Contains(scenario.Logs.Entries, entry => entry.Contains("[Warning]", StringComparison.Ordinal)
            && entry.Contains(oldClone, StringComparison.Ordinal) && entry.Contains(newRoot, StringComparison.Ordinal));
    }

    [Fact]
    public async Task no_warning_is_logged_while_every_clone_lies_under_the_workspace_root()
    {
        using var scenario = new WorkflowScenario();
        await using var host = new WorkflowHost(scenario);
        await new WorkflowApi(host.CreateClient(), scenario.Logs).RegisterRepositoryAsync(scenario);
        await using var restarted = new WorkflowHost(scenario);
        _ = restarted.CreateClient();

        Assert.DoesNotContain(scenario.Logs.Entries, entry => entry.Contains("workspace root", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task ChangeGlobalDirectoriesAsync(WorkflowHost host, WorkflowScenario scenario, string? workspaceRoot = null)
    {
        HttpClient client = host.CreateClient();
        var global = (await client.GetFromJsonAsync<JsonElement>("/api/settings/global", Ct)).Deserialize<Dictionary<string, object?>>()!;
        global["workspaceRootDirectory"] = workspaceRoot ?? Path.Combine(scenario.Sandbox.Root, "runtime-changed-workspaces");
        global["copilotBaseDirectory"] = Path.Combine(scenario.Sandbox.Root, "runtime-changed-copilot");
        using HttpResponseMessage response = await client.PutAsJsonAsync("/api/settings/global", global, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<EffectiveSettings> EffectiveAsync(WorkflowHost host, int repositoryId)
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IEffectiveSettingsProvider>().GetAsync(repositoryId, Ct);
    }
}
