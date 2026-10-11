using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Orchestration.Recovery.Startup;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Prerequisites;

namespace WebDevLoop.Web.Tests.Workflow;

public sealed class ModelCatalogScenarioTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task a_never_fetched_model_catalog_keeps_settings_reachable_but_prevents_agent_launches(bool previousProcessFetched)
    {
        using var scenario = new WorkflowScenario();
        if (previousProcessFetched)
        {
            await using var previous = new WorkflowHost(scenario);
            Assert.Equal(HttpStatusCode.OK, (await previous.CreateClient().GetAsync("/api/health", Ct)).StatusCode);
        }

        await using var host = new WorkflowHost(scenario);
        host.Models.Failure = "Copilot is unavailable.";
        HttpClient client = host.CreateClient();

        using HttpResponseMessage health = await client.GetAsync("/api/health", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, health.StatusCode);
        JsonElement body = await health.Content.ReadFromJsonAsync<JsonElement>(Ct);
        JsonElement check = Assert.Single(body.GetProperty("failedChecks").EnumerateArray());
        Assert.Equal("Agent models", check.GetProperty("name").GetString());
        Assert.Contains("never", check.GetProperty("message").GetString());
        Assert.Contains("/settings", check.GetProperty("remediation").GetString());
        Assert.False(host.Models.Catalog.HasFetched);
        Assert.Empty(host.Models.Catalog.Models);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/settings", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await client.PostAsJsonAsync("/api/repos/1/spec-runs", new { specIssueNumber = 100 }, Ct)).StatusCode);
        Assert.False(host.Services.GetRequiredService<SchedulerStartGate>().IsOpen);
        Assert.Empty(scenario.Journal.Calls);
    }

    [Fact]
    public async Task startup_fetches_the_model_catalog_before_evaluating_agent_models()
    {
        using var scenario = new WorkflowScenario();
        await using var host = new WorkflowHost(scenario);
        HttpClient client = host.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health", Ct)).StatusCode);
        Assert.True(host.Models.Catalog.HasFetched);
        Assert.Contains(host.Models.Catalog.Models, model =>
            model.Id == "claude-sonnet-5" && model.DisplayName == "Claude Sonnet 5"
            && model.SupportedReasoningEfforts.Contains("high"));
    }

    [Fact]
    public async Task invalid_global_and_repository_models_report_every_role_and_effective_scope_without_launching()
    {
        using var scenario = new WorkflowScenario();
        int repositoryId;
        await using (var setup = new WorkflowHost(scenario))
        {
            HttpClient client = setup.CreateClient();
            repositoryId = await new WorkflowApi(client).RegisterRepositoryAsync(scenario);
            using HttpResponseMessage global = await client.PutAsJsonAsync("/api/settings/global", new SettingsProfileData
            {
                Roles = Enum.GetValues<AgentRole>().ToDictionary(role => role, _ => new RoleSettingsOverride { Model = "missing-global-model" }),
            }, Ct);
            Assert.Equal(HttpStatusCode.OK, global.StatusCode);
            using HttpResponseMessage repository = await client.PutAsJsonAsync($"/api/repos/{repositoryId}/settings", new SettingsProfileData
            {
                Roles = new Dictionary<AgentRole, RoleSettingsOverride>
                {
                    [AgentRole.Explorer] = new() { Model = "claude-sonnet-5" },
                    [AgentRole.Implementer] = new() { Model = "missing-repository-model" },
                },
            }, Ct);
            Assert.Equal(HttpStatusCode.OK, repository.StatusCode);
        }

        await using var host = new WorkflowHost(scenario);
        HttpClient diagnosticClient = host.CreateClient();
        using HttpResponseMessage health = await diagnosticClient.GetAsync("/api/health", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, health.StatusCode);
        JsonElement body = await health.Content.ReadFromJsonAsync<JsonElement>(Ct);
        JsonElement check = Assert.Single(body.GetProperty("failedChecks").EnumerateArray());
        string message = check.GetProperty("message").GetString()!;
        foreach (AgentRole role in Enum.GetValues<AgentRole>())
        {
            Assert.Contains($"Global settings: {role}, Model 'missing-global-model'", message);
            if (role != AgentRole.Explorer)
            {
                Assert.Contains($"acme/widgets: {role}, Model", message);
            }
        }

        Assert.Contains("acme/widgets: Implementer, Model 'missing-repository-model'", message);
        Assert.DoesNotContain("acme/widgets: Explorer", message);
        Assert.Contains("/settings", check.GetProperty("remediation").GetString());
        Assert.Contains("href=\"/settings#section-roles\"", await diagnosticClient.GetStringAsync("/health", Ct));
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await diagnosticClient.PostAsJsonAsync($"/api/repos/{repositoryId}/spec-runs", new { specIssueNumber = 100 }, Ct)).StatusCode);
        Assert.False(host.Services.GetRequiredService<SchedulerStartGate>().IsOpen);
        Assert.Empty(scenario.Journal.Calls);
    }

    [Fact]
    public async Task unsupported_reasoning_efforts_are_validated_globally_and_in_every_repository()
    {
        using var scenario = new WorkflowScenario();
        int repositoryId;
        await using (var setup = new WorkflowHost(scenario))
        {
            HttpClient client = setup.CreateClient();
            await new WorkflowApi(client).RegisterRepositoryAsync(scenario);
            using HttpResponseMessage registered = await client.PostAsJsonAsync("/api/repos",
                new { owner = "acme", name = "other", cloneUrl = scenario.Sandbox.RemotePath }, Ct);
            Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
            repositoryId = (await registered.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt32();
            using HttpResponseMessage global = await client.PutAsJsonAsync("/api/settings/global", new SettingsProfileData
            {
                Roles = new Dictionary<AgentRole, RoleSettingsOverride>
                {
                    [AgentRole.Explorer] = new() { ReasoningEffort = "low" },
                },
            }, Ct);
            Assert.Equal(HttpStatusCode.OK, global.StatusCode);
            using HttpResponseMessage repository = await client.PutAsJsonAsync($"/api/repos/{repositoryId}/settings", new SettingsProfileData
            {
                Roles = new Dictionary<AgentRole, RoleSettingsOverride>
                {
                    [AgentRole.Explorer] = new() { ReasoningEffort = "medium" },
                    [AgentRole.Tester] = new() { Model = "no-reasoning-model" },
                    [AgentRole.ReviewerSpecification] = new() { ReasoningEffort = "max" },
                },
            }, Ct);
            Assert.Equal(HttpStatusCode.OK, repository.StatusCode);
        }

        await using var host = new WorkflowHost(scenario);
        host.Models.Models = [.. host.Models.Models, new("no-reasoning-model", "No reasoning", [])];
        HttpClient diagnosticClient = host.CreateClient();
        using HttpResponseMessage health = await diagnosticClient.GetAsync("/api/health", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, health.StatusCode);
        JsonElement body = await health.Content.ReadFromJsonAsync<JsonElement>(Ct);
        JsonElement check = Assert.Single(body.GetProperty("failedChecks").EnumerateArray());
        string message = check.GetProperty("message").GetString()!;
        Assert.Contains("Global settings: Explorer, Model 'claude-sonnet-5', Reasoning Effort 'low'", message);
        Assert.Contains("acme/widgets: Explorer, Model 'claude-sonnet-5', Reasoning Effort 'low'", message);
        Assert.DoesNotContain("acme/other: Tester", message);
        Assert.Contains("acme/other: ReviewerSpecification, Model 'claude-opus-5', Reasoning Effort 'max'", message);
        Assert.DoesNotContain("acme/other: Explorer", message);
        Assert.Equal(HttpStatusCode.OK, (await diagnosticClient.GetAsync("/settings", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await diagnosticClient.PostAsJsonAsync($"/api/repos/{repositoryId}/spec-runs", new { specIssueNumber = 100 }, Ct)).StatusCode);
        Assert.False(host.Services.GetRequiredService<SchedulerStartGate>().IsOpen);
        Assert.Empty(scenario.Journal.Calls);
    }

    [Fact]
    public async Task missing_github_sign_in_is_reported_before_a_never_fetched_catalog()
    {
        using var scenario = new WorkflowScenario();
        await using var host = new WorkflowHost(scenario);
        host.SignIn.Login = null;
        host.Models.Failure = "Nobody is signed in.";
        HttpClient client = host.CreateClient();

        using HttpResponseMessage health = await client.GetAsync("/api/health", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, health.StatusCode);
        JsonElement body = await health.Content.ReadFromJsonAsync<JsonElement>(Ct);
        JsonElement failed = Assert.Single(body.GetProperty("failedChecks").EnumerateArray());
        Assert.Equal("GitHub authentication", failed.GetProperty("name").GetString());
        Assert.Contains("Nobody is signed in", failed.GetProperty("message").GetString());
        Assert.False(host.Services.GetRequiredService<SchedulerStartGate>().IsOpen);
        Assert.Empty(scenario.Journal.Calls);
    }

    [Theory]
    [InlineData("missing-model", "high")]
    [InlineData("claude-sonnet-5", "low")]
    public async Task one_invalid_repository_role_blocks_the_whole_app_when_global_settings_are_valid(string model, string reasoningEffort)
    {
        using var scenario = new WorkflowScenario();
        int repositoryId;
        await using (var setup = new WorkflowHost(scenario))
        {
            HttpClient client = setup.CreateClient();
            repositoryId = await new WorkflowApi(client).RegisterRepositoryAsync(scenario);
            using HttpResponseMessage saved = await client.PutAsJsonAsync($"/api/repos/{repositoryId}/settings", new SettingsProfileData
            {
                Roles = new Dictionary<AgentRole, RoleSettingsOverride>
                {
                    [AgentRole.Implementer] = new() { Model = model, ReasoningEffort = reasoningEffort },
                },
            }, Ct);
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        }

        await using var host = new WorkflowHost(scenario);
        HttpClient diagnosticClient = host.CreateClient();
        using HttpResponseMessage health = await diagnosticClient.GetAsync("/api/health", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, health.StatusCode);
        JsonElement body = await health.Content.ReadFromJsonAsync<JsonElement>(Ct);
        string message = Assert.Single(body.GetProperty("failedChecks").EnumerateArray()).GetProperty("message").GetString()!;
        Assert.Contains($"acme/widgets: Implementer, Model '{model}'", message);
        Assert.DoesNotContain("Global settings", message);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await diagnosticClient.PostAsJsonAsync($"/api/repos/{repositoryId}/spec-runs", new { specIssueNumber = 100 }, Ct)).StatusCode);
        Assert.False(host.Services.GetRequiredService<SchedulerStartGate>().IsOpen);
        Assert.Empty(scenario.Journal.Calls);
    }

    [Fact]
    public async Task a_failed_refresh_keeps_the_last_known_catalog_and_work_can_still_launch()
    {
        using var scenario = new WorkflowScenario(101);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(101, "Ticket", parent: 100);
        await using var host = new WorkflowHost(scenario);
        HttpClient client = host.CreateClient();
        var api = new WorkflowApi(client, scenario.Logs);
        int repositoryId = await api.RegisterRepositoryAsync(scenario);
        host.Models.Models = [];
        host.Models.Failure = "Transient Copilot outage.";

        ModelCatalogRefreshResult refreshed = await host.Models.Catalog.RefreshAsync(Ct);
        Assert.False(refreshed.Succeeded);
        Assert.Equal("Transient Copilot outage.", refreshed.Error);
        await host.Services.GetRequiredService<DiagnosticReadiness>().RefreshAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health", Ct)).StatusCode);
        Assert.True(host.Models.Catalog.HasFetched);
        Assert.Contains(host.Models.Catalog.Models, model => model.Id == "claude-sonnet-5"
            && model.SupportedReasoningEfforts.Contains("high"));
        string run = await api.EnqueueAsync(repositoryId, 100);
        await api.WaitForStatusAsync(run, nameof(SpecRunStatus.AwaitingMerge));
        Assert.Contains(scenario.Journal.Calls, call => call.Role == AgentRole.Implementer);
    }

    [Fact]
    public async Task models_without_reasoning_support_allow_inherited_efforts_and_agent_launches()
    {
        using var scenario = new WorkflowScenario(101);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(101, "Ticket", parent: 100);
        int repositoryId;
        await using (var setup = new WorkflowHost(scenario))
        {
            HttpClient client = setup.CreateClient();
            repositoryId = await new WorkflowApi(client).RegisterRepositoryAsync(scenario);
            using HttpResponseMessage saved = await client.PutAsJsonAsync("/api/settings/global", new SettingsProfileData
            {
                Roles = Enum.GetValues<AgentRole>().ToDictionary(role => role,
                    _ => new RoleSettingsOverride { Model = "no-reasoning-model" }),
            }, Ct);
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        }

        await using var host = new WorkflowHost(scenario);
        host.Models.Models = [.. host.Models.Models, new("no-reasoning-model", "No reasoning", [])];
        HttpClient operationalClient = host.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await operationalClient.GetAsync("/api/health", Ct)).StatusCode);
        var api = new WorkflowApi(operationalClient, scenario.Logs);
        string run = await api.EnqueueAsync(repositoryId, 100);
        await api.WaitForStatusAsync(run, nameof(SpecRunStatus.AwaitingMerge));
        Assert.Contains(scenario.Journal.Calls, call => call.Role == AgentRole.Implementer);
    }

    [Fact]
    public async Task an_empty_successful_catalog_is_fetched_but_cannot_validate_any_configured_model()
    {
        using var scenario = new WorkflowScenario();
        await using var host = new WorkflowHost(scenario);
        host.Models.Models = [];
        HttpClient client = host.CreateClient();

        using HttpResponseMessage health = await client.GetAsync("/api/health", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, health.StatusCode);
        JsonElement body = await health.Content.ReadFromJsonAsync<JsonElement>(Ct);
        string message = Assert.Single(body.GetProperty("failedChecks").EnumerateArray()).GetProperty("message").GetString()!;
        Assert.Contains("Global settings: Explorer, Model 'claude-sonnet-5'", message);
        Assert.DoesNotContain("never", message);
        Assert.True(host.Models.Catalog.HasFetched);
        Assert.Empty(host.Models.Catalog.Models);
        Assert.Empty(scenario.Journal.Calls);
    }
}
