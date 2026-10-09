using System.Net;
using System.Text.Json;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Web.Tests.Api;

public sealed class SettingsEndpointTests
{
    private static readonly EffectiveSettingsView Effective = new(
        "/ws", "/copilot", "main", 1, SpecDependencyMode.WaitForMerge, 4, 2, 5, 2, 3, 3, "run it", new PortRangeData(41000, 41999),
        new Dictionary<AgentRole, EffectiveRoleSettingsView> { [AgentRole.Tester] = new("model-x", "high", "Open {app_url}", 3600) });

    [Fact]
    public async Task global_settings_are_returned_with_string_enums()
    {
        await using var factory = new ApiFactory();
        factory.Settings.Global = new SettingsProfileData
        {
            SpecDependencyMode = SpecDependencyMode.StackOnTop,
            Roles = new Dictionary<AgentRole, RoleSettingsOverride> { [AgentRole.Tester] = new(PromptTemplate: "Open {app_url}") },
        };
        using HttpClient client = factory.CreateClient();

        JsonElement body = await client.GetJsonAsync("/api/settings/global");

        Assert.Equal("StackOnTop", body.GetProperty("specDependencyMode").GetString());
        Assert.Equal("Open {app_url}", body.GetProperty("roles").GetProperty("Tester").GetProperty("promptTemplate").GetString());
    }

    [Fact]
    public async Task putting_global_settings_forwards_the_whole_layer_including_prompt_templates()
    {
        await using var factory = new ApiFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PutJsonAsync("/api/settings/global", new
        {
            maxRetries = 5,
            baseBranch = "develop",
            specDependencyMode = "StackOnTop",
            testPortRange = new { start = 42000, end = 42100 },
            roles = new Dictionary<string, object> { ["Implementer"] = new { model = "m", promptTemplate = "Implement {ticket_title}", timeoutSeconds = 60 } },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SettingsProfileData saved = Assert.Single(factory.Settings.SavedGlobal);
        Assert.Equal((5, "develop", SpecDependencyMode.StackOnTop, new PortRangeData(42000, 42100)), (saved.MaxRetries, saved.BaseBranch, saved.SpecDependencyMode, saved.TestPortRange));
        Assert.Equal(new RoleSettingsOverride("m", null, "Implement {ticket_title}", 60), saved.Roles![AgentRole.Implementer]);
        Assert.Equal(5, (await response.ReadJsonAsync()).GetProperty("maxRetries").GetInt32());
    }

    [Fact]
    public async Task invalid_global_settings_return_field_errors()
    {
        await using var factory = new ApiFactory();
        factory.Settings.SaveGlobalResult = CommandResult<SettingsProfileData>.Invalid(
        [
            new SettingsValidationError("MaxRetries", "Must be at least 0, but was -1."),
            new SettingsValidationError("Roles.Tester.PromptTemplate", "Unknown placeholder {x}."),
        ]);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PutJsonAsync("/api/settings/global", new { maxRetries = -1 });
        JsonElement errors = (await response.ReadJsonAsync()).GetProperty("errors");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Must be at least 0, but was -1.", errors.GetProperty("MaxRetries")[0].GetString());
        Assert.Equal("Unknown placeholder {x}.", errors.GetProperty("Roles.Tester.PromptTemplate")[0].GetString());
    }

    [Fact]
    public async Task repository_settings_can_be_read_and_replaced()
    {
        await using var factory = new ApiFactory();
        using HttpClient client = factory.CreateClient();

        JsonElement read = await client.GetJsonAsync("/api/repos/4/settings");
        using HttpResponseMessage put = await client.PutJsonAsync("/api/repos/4/settings", new { maxActiveSpecsPerRepo = 3 });

        Assert.Equal(3, read.GetProperty("maxActiveSpecsPerRepo").GetInt32());
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal((4, 3), (factory.Settings.SavedRepository.Single().Id, factory.Settings.SavedRepository.Single().Data.MaxActiveSpecsPerRepo));
    }

    [Fact]
    public async Task repository_settings_of_an_unknown_repository_return_404_and_invalid_ones_400()
    {
        await using var factory = new ApiFactory();
        using HttpClient client = factory.CreateClient();

        factory.Settings.RepositoryResult = CommandResult<SettingsProfileData>.NotFound("no repo");
        using HttpResponseMessage missingRead = await client.GetResponseAsync("/api/repos/9/settings");
        using HttpResponseMessage missingPut = await client.PutJsonAsync("/api/repos/9/settings", new { maxRetries = 1 });

        factory.Settings.RepositoryResult = CommandResult<SettingsProfileData>.Invalid([new SettingsValidationError("MaxConcurrentImplementersGlobal", "Only global.")]);
        using HttpResponseMessage invalid = await client.PutJsonAsync("/api/repos/4/settings", new { maxConcurrentImplementersGlobal = 8 });

        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.BadRequest], [missingRead.StatusCode, missingPut.StatusCode, invalid.StatusCode]);
    }

    [Fact]
    public async Task effective_settings_expose_every_resolved_value_and_role_prompt()
    {
        await using var factory = new ApiFactory();
        factory.Settings.EffectiveResult = CommandResult<EffectiveSettingsView>.Succeeded(Effective);
        using HttpClient client = factory.CreateClient();

        JsonElement body = await client.GetJsonAsync("/api/settings/effective/4");

        Assert.Equal(("main", "WaitForMerge", 41000), (body.GetProperty("baseBranch").GetString(), body.GetProperty("specDependencyMode").GetString(), body.GetProperty("testPortRange").GetProperty("start").GetInt32()));
        JsonElement tester = body.GetProperty("roles").GetProperty("Tester");
        Assert.Equal(("model-x", "Open {app_url}", 3600), (tester.GetProperty("model").GetString(), tester.GetProperty("promptTemplate").GetString(), tester.GetProperty("timeoutSeconds").GetInt32()));
    }

    [Theory]
    [InlineData(CommandStatus.NotFound, HttpStatusCode.NotFound)]
    [InlineData(CommandStatus.Conflict, HttpStatusCode.Conflict)]
    public async Task effective_settings_failures_map_to_status_codes(CommandStatus status, HttpStatusCode expected)
    {
        await using var factory = new ApiFactory();
        factory.Settings.EffectiveResult = new CommandResult<EffectiveSettingsView>(status, Message: "nope");
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetResponseAsync("/api/settings/effective/4");

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task settings_stay_editable_in_diagnostic_only_mode()
    {
        await using ApiFactory factory = await new ApiFactory().EvaluateAsync(new WebDevLoop.Core.Ports.PrerequisiteCheck("Git", WebDevLoop.Core.Ports.PrerequisiteStatus.Failed, "missing"));
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PutJsonAsync("/api/settings/global", new { maxRetries = 1 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
