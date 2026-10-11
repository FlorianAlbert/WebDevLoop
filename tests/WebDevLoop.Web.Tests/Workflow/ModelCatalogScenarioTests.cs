using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Orchestration.Recovery.Startup;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Prerequisites;
using WebDevLoop.Web.Background;

namespace WebDevLoop.Web.Tests.Workflow;

public sealed class ModelCatalogScenarioTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task manual_refresh_pauses_on_model_loss_and_resumes_when_the_catalog_is_valid_again()
    {
        using var scenario = new WorkflowScenario(101);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(101, "Ticket", parent: 100);
        await using var host = new WorkflowHost(scenario);
        HttpClient client = host.CreateClient();
        var api = new WorkflowApi(client, scenario.Logs);
        int repositoryId = await api.RegisterRepositoryAsync(scenario);
        SchedulerStartGate gate = host.Services.GetRequiredService<SchedulerStartGate>();
        await gate.WaitUntilOpenAsync(Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);
        ModelCatalogReadiness models = host.Services.GetRequiredService<ModelCatalogReadiness>();
        var available = host.Models.Models;
        host.Models.Models = [];

        Assert.True((await models.RefreshAsync(Ct)).Succeeded);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/health", Ct)).StatusCode);
        Assert.False(gate.IsOpen);
        Assert.Empty(scenario.Journal.Calls);
        host.Models.Models = available;

        Assert.True((await models.RefreshAsync(Ct)).Succeeded);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health", Ct)).StatusCode);
        string run = await api.EnqueueAsync(repositoryId, 100);
        await api.WaitForStatusAsync(run, nameof(SpecRunStatus.AwaitingMerge));
        Assert.Contains(scenario.Journal.Calls, call => call.Role == AgentRole.Implementer);
    }

    [Fact]
    public async Task github_sign_in_refetches_models_and_revalidates_before_resuming_work()
    {
        using var scenario = new WorkflowScenario(101);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(101, "Ticket", parent: 100);
        await using var host = new WorkflowHost(scenario);
        host.SignIn.Login = null;
        host.Models.Failure = "Nobody is signed in.";
        HttpClient client = host.CreateClient();
        var api = new WorkflowApi(client, scenario.Logs);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/health", Ct)).StatusCode);

        host.Models.Failure = null;
        host.SignIn.ChangeLogin("test-user");
        await api.WaitForHealthStatusAsync(HttpStatusCode.OK);
        int repositoryId = await api.RegisterRepositoryAsync(scenario);
        string run = await api.EnqueueAsync(repositoryId, 100);
        await api.WaitForStatusAsync(run, nameof(SpecRunStatus.AwaitingMerge));
        Assert.Contains(scenario.Journal.Calls, call => call.Role == AgentRole.Implementer);
    }

    [Fact]
    public async Task a_retry_within_an_in_flight_phase_waits_for_model_validation_to_pass()
    {
        using var scenario = new WorkflowScenario(101);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(101, "Ticket", parent: 100);
        var script = new AgentScript();
        script.HangingImplementers.Add(101);
        script.FailFirstImplementation.Add(101);
        await using var host = new WorkflowHost(scenario, script);
        HttpClient client = host.CreateClient();
        var api = new WorkflowApi(client, scenario.Logs);
        int repositoryId = await api.RegisterRepositoryAsync(scenario);
        string run = await api.EnqueueAsync(repositoryId, 100);
        await script.HangingImplementerStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        using HttpResponseMessage invalid = await client.PutAsJsonAsync("/api/settings/global", new SettingsProfileData
        {
            Roles = new Dictionary<AgentRole, RoleSettingsOverride>
            {
                [AgentRole.Implementer] = new() { Model = "unavailable-model" },
            },
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        int launches = scenario.Journal.Calls.Count;
        script.ReleaseHangingImplementer.TrySetResult();
        await api.WaitForTicketStepStatusAsync(run, nameof(StepStatus.Failed));
        await Task.Delay(TimeSpan.FromMilliseconds(750), Ct);
        Assert.Equal(launches, scenario.Journal.Calls.Count);
        Assert.Equal(0, scenario.Journal.ActiveImplementers);

        using HttpResponseMessage fixedSettings = await client.PutAsJsonAsync("/api/settings/global", new SettingsProfileData(), Ct);
        Assert.Equal(HttpStatusCode.OK, fixedSettings.StatusCode);
        await api.WaitForStatusAsync(run, nameof(SpecRunStatus.AwaitingMerge));
        Assert.Equal(2, scenario.Journal.Calls.Count(call => call.Role == AgentRole.Implementer));
    }

    [Fact]
    public async Task in_flight_steps_finish_when_validation_fails_and_pending_work_resumes_after_settings_are_fixed()
    {
        using var scenario = new WorkflowScenario(101);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(101, "Ticket", parent: 100);
        var script = new AgentScript();
        script.HangingImplementers.Add(101);
        await using var host = new WorkflowHost(scenario, script);
        HttpClient client = host.CreateClient();
        var api = new WorkflowApi(client, scenario.Logs);
        int repositoryId = await api.RegisterRepositoryAsync(scenario);
        string run = await api.EnqueueAsync(repositoryId, 100);
        await script.HangingImplementerStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        using HttpResponseMessage invalid = await client.PutAsJsonAsync("/api/settings/global", new SettingsProfileData
        {
            Roles = new Dictionary<AgentRole, RoleSettingsOverride>
            {
                [AgentRole.ReviewerCodingStandards] = new() { Model = "unavailable-model" },
            },
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/health", Ct)).StatusCode);
        int launches = scenario.Journal.Calls.Count;
        script.ReleaseHangingImplementer.TrySetResult();
        await api.WaitForTicketStatusAsync(run, 101, nameof(TicketRunStatus.Implementing), untilDifferent: true);
        await Task.Delay(TimeSpan.FromMilliseconds(750), Ct);
        Assert.Equal(launches, scenario.Journal.Calls.Count);
        Assert.Equal(0, scenario.Journal.ActiveImplementers);
        JsonElement ticket = Assert.Single(await api.TicketsAsync(run));
        Assert.Equal(nameof(TicketRunStatus.Reviewing), ticket.GetProperty("status").GetString());

        using HttpResponseMessage fixedSettings = await client.PutAsJsonAsync("/api/settings/global", new SettingsProfileData(), Ct);
        Assert.Equal(HttpStatusCode.OK, fixedSettings.StatusCode);
        await api.WaitForStatusAsync(run, nameof(SpecRunStatus.AwaitingMerge));
        Assert.Single(scenario.Journal.Calls, call => call.Role == AgentRole.Implementer);
        Assert.Contains(scenario.Journal.Calls, call => call.Role == AgentRole.ReviewerCodingStandards);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task fixing_settings_reopens_the_gate_and_resumes_work_without_restarting(bool repositoryOverride)
    {
        using var scenario = new WorkflowScenario(101);
        scenario.Issues.Seed(100, "Spec");
        scenario.Issues.Seed(101, "Ticket", parent: 100);
        await using var host = new WorkflowHost(scenario);
        HttpClient client = host.CreateClient();
        var api = new WorkflowApi(client, scenario.Logs);
        int repositoryId = await api.RegisterRepositoryAsync(scenario);
        SchedulerStartGate gate = host.Services.GetRequiredService<SchedulerStartGate>();
        await gate.WaitUntilOpenAsync(Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);
        string settingsUrl = repositoryOverride ? $"/api/repos/{repositoryId}/settings" : "/api/settings/global";

        using HttpResponseMessage invalid = await client.PutAsJsonAsync(settingsUrl, new SettingsProfileData
        {
            Roles = new Dictionary<AgentRole, RoleSettingsOverride>
            {
                [AgentRole.Implementer] = new() { Model = "unavailable-model" },
            },
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/health", Ct)).StatusCode);
        Assert.False(gate.IsOpen);
        Assert.Empty(scenario.Journal.Calls);

        host.Models.Models = [.. host.Models.Models, new("replacement-model", "Replacement Model", ["high"])];
        using HttpResponseMessage fixedSettings = await client.PutAsJsonAsync(settingsUrl, new SettingsProfileData
        {
            Roles = new Dictionary<AgentRole, RoleSettingsOverride>
            {
                [AgentRole.Implementer] = new() { Model = "replacement-model" },
            },
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, fixedSettings.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health", Ct)).StatusCode);
        string run = await api.EnqueueAsync(repositoryId, 100);
        await api.WaitForStatusAsync(run, nameof(SpecRunStatus.AwaitingMerge));
        Assert.Contains(scenario.Journal.Calls, call => call.Role == AgentRole.Implementer);
    }

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
            model.Id == "claude-sonnet-5.5" && model.DisplayName == "Claude Sonnet 5.5"
            && model.SupportedReasoningEfforts.Contains("high"));
    }

    [Theory]
    [InlineData("claude-sonnet-5.5", AgentRole.Implementer, AgentRole.ReviewerSpecification)]
    [InlineData("claude-opus-5.5", AgentRole.ReviewerSpecification, AgentRole.Implementer)]
    public async Task a_missing_default_model_is_kept_and_the_diagnostic_tells_the_user_to_choose_a_model(
        string missingModel, AgentRole affectedRole, AgentRole availableRole)
    {
        using var scenario = new WorkflowScenario();
        await using var host = new WorkflowHost(scenario);
        host.Models.Models =
        [
            .. host.Models.Models.Where(model => model.Id != missingModel),
            new("alternative-model", "Available alternative", ["medium", "high"]),
        ];
        HttpClient client = host.CreateClient();

        using HttpResponseMessage health = await client.GetAsync("/api/health", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, health.StatusCode);
        JsonElement body = await health.Content.ReadFromJsonAsync<JsonElement>(Ct);
        JsonElement check = Assert.Single(body.GetProperty("failedChecks").EnumerateArray());
        Assert.Equal("Agent models", check.GetProperty("name").GetString());
        string message = check.GetProperty("message").GetString()!;
        Assert.Contains($"Global settings: {affectedRole}, Model '{missingModel}' is not in the Model Catalog.", message);
        Assert.DoesNotContain($"Global settings: {availableRole},", message);
        Assert.Contains("Choose available Models", check.GetProperty("remediation").GetString());
        Assert.Contains("/settings#section-roles", check.GetProperty("remediation").GetString());

        JsonElement stored = await client.GetFromJsonAsync<JsonElement>("/api/settings/global", Ct);
        Assert.Equal(missingModel, stored.GetProperty("roles").GetProperty(affectedRole.ToString()).GetProperty("model").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/settings", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await client.PostAsJsonAsync("/api/repos/1/spec-runs", new { specIssueNumber = 100 }, Ct)).StatusCode);
        Assert.False(host.Services.GetRequiredService<SchedulerStartGate>().IsOpen);
        Assert.Empty(scenario.Journal.Calls);
    }

    [Theory]
    [InlineData("claude-sonnet-5", "claude-opus-5")]
    [InlineData("claude-opus-5", "claude-sonnet-5")]
    public async Task stored_global_and_repository_model_ids_survive_restart_without_migration(
        string globalModel, string repositoryModel)
    {
        using var scenario = new WorkflowScenario();
        int repositoryId;
        await using (var setup = new WorkflowHost(scenario))
        {
            HttpClient client = setup.CreateClient();
            repositoryId = await new WorkflowApi(client).RegisterRepositoryAsync(scenario);
            using HttpResponseMessage global = await client.PutAsJsonAsync("/api/settings/global", new SettingsProfileData
            {
                Roles = Enum.GetValues<AgentRole>().ToDictionary(role => role,
                    _ => new RoleSettingsOverride { Model = globalModel }),
            }, Ct);
            Assert.Equal(HttpStatusCode.OK, global.StatusCode);
            using HttpResponseMessage repository = await client.PutAsJsonAsync($"/api/repos/{repositoryId}/settings", new SettingsProfileData
            {
                Roles = new Dictionary<AgentRole, RoleSettingsOverride>
                {
                    [AgentRole.Implementer] = new() { Model = repositoryModel },
                },
            }, Ct);
            Assert.Equal(HttpStatusCode.OK, repository.StatusCode);
        }

        await using var host = new WorkflowHost(scenario);
        host.Models.Models =
        [
            new("claude-sonnet-5", "Claude Sonnet 5", ["medium", "high"]),
            new("claude-opus-5", "Claude Opus 5", ["medium", "high"]),
        ];
        HttpClient restartedClient = host.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await restartedClient.GetAsync("/api/health", Ct)).StatusCode);
        JsonElement storedGlobal = await restartedClient.GetFromJsonAsync<JsonElement>("/api/settings/global", Ct);
        Assert.All(Enum.GetValues<AgentRole>(), role =>
            Assert.Equal(globalModel, storedGlobal.GetProperty("roles").GetProperty(role.ToString()).GetProperty("model").GetString()));
        JsonElement storedRepository = await restartedClient.GetFromJsonAsync<JsonElement>($"/api/repos/{repositoryId}/settings", Ct);
        Assert.Equal(repositoryModel, storedRepository.GetProperty("roles").GetProperty("Implementer").GetProperty("model").GetString());
        JsonElement effective = await restartedClient.GetFromJsonAsync<JsonElement>($"/api/settings/effective/{repositoryId}", Ct);
        Assert.All(Enum.GetValues<AgentRole>(), role => Assert.Equal(
            role == AgentRole.Implementer ? repositoryModel : globalModel,
            effective.GetProperty("roles").GetProperty(role.ToString()).GetProperty("model").GetString()));
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
                    [AgentRole.Explorer] = new() { Model = "claude-sonnet-5.5" },
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
        Assert.Contains("Global settings: Explorer, Model 'claude-sonnet-5.5', Reasoning Effort 'low'", message);
        Assert.Contains("acme/widgets: Explorer, Model 'claude-sonnet-5.5', Reasoning Effort 'low'", message);
        Assert.DoesNotContain("acme/other: Tester", message);
        Assert.Contains("acme/other: ReviewerSpecification, Model 'claude-opus-5.5', Reasoning Effort 'max'", message);
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
    [InlineData("claude-sonnet-5.5", "low")]
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

        ModelCatalogRefreshResult refreshed = await host.Services.GetRequiredService<ModelCatalogReadiness>().RefreshAsync(Ct);
        Assert.False(refreshed.Succeeded);
        Assert.Equal("Transient Copilot outage.", refreshed.Error);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health", Ct)).StatusCode);
        Assert.True(host.Models.Catalog.HasFetched);
        Assert.Contains(host.Models.Catalog.Models, model => model.Id == "claude-sonnet-5.5"
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
        Assert.Contains("Global settings: Explorer, Model 'claude-sonnet-5.5'", message);
        Assert.DoesNotContain("never", message);
        Assert.True(host.Models.Catalog.HasFetched);
        Assert.Empty(host.Models.Catalog.Models);
        Assert.Empty(scenario.Journal.Calls);
    }
}
