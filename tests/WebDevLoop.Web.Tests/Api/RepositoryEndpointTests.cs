using System.Net;
using System.Text.Json;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Web.Tests.Api;

public sealed class RepositoryEndpointTests
{
    [Fact]
    public async Task repositories_are_listed_and_fetched_by_id()
    {
        await using var factory = new ApiFactory();
        factory.Repositories.Repositories.AddRange([ApiData.Repository(1, "widgets"), ApiData.Repository(2, "gadgets", enabled: false)]);
        using HttpClient client = factory.CreateClient();

        JsonElement list = await client.GetJsonAsync("/api/repos");
        JsonElement one = await client.GetJsonAsync("/api/repos/2");

        Assert.Equal(["widgets", "gadgets"], list.EnumerateArray().Select(item => item.GetProperty("name").GetString()));
        Assert.Equal(("acme", "gadgets", false), (one.GetProperty("owner").GetString(), one.GetProperty("name").GetString(), one.GetProperty("isEnabled").GetBoolean()));
    }

    [Fact]
    public async Task an_unknown_repository_is_not_found_with_a_problem_body()
    {
        await using var factory = new ApiFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetResponseAsync("/api/repos/99");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(404, (await response.ReadJsonAsync()).GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task registering_a_repository_returns_201_with_location_and_forwards_the_command()
    {
        await using var factory = new ApiFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostJsonAsync("/api/repos", new { owner = "acme", name = "widgets", localPath = "/work/w", defaultBaseBranch = "develop" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/repos/1", response.Headers.Location!.OriginalString);
        Assert.Equal("widgets", (await response.ReadJsonAsync()).GetProperty("name").GetString());
        Assert.Equal(new RegisterRepositoryCommand("acme", "widgets", "/work/w", "develop"), Assert.Single(factory.Registry.Registered));
    }

    [Fact]
    public async Task registering_an_invalid_repository_returns_a_validation_problem_with_field_errors()
    {
        await using var factory = new ApiFactory();
        factory.Registry.RegisterResult = CommandResult<WebDevLoop.Core.Queries.RepositoryView>.Invalid([new SettingsValidationError("LocalPath", "A value is required.")]);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostJsonAsync("/api/repos", new { owner = "acme", name = "widgets" });
        JsonElement body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("A value is required.", body.GetProperty("errors").GetProperty("LocalPath")[0].GetString());
    }

    [Fact]
    public async Task registering_a_duplicate_repository_returns_409()
    {
        await using var factory = new ApiFactory();
        factory.Registry.RegisterResult = CommandResult<WebDevLoop.Core.Queries.RepositoryView>.Conflict("already registered");
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostJsonAsync("/api/repos", new { owner = "acme", name = "widgets", localPath = "/w" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("already registered", (await response.ReadJsonAsync()).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task patching_a_repository_forwards_only_the_supplied_fields()
    {
        await using var factory = new ApiFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PatchJsonAsync("/api/repos/1", new { isEnabled = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((1, new UpdateRepositoryCommand(IsEnabled: false)), Assert.Single(factory.Registry.Updated));
    }

    [Fact]
    public async Task patching_an_unknown_repository_returns_404()
    {
        await using var factory = new ApiFactory();
        factory.Registry.UpdateResult = CommandResult<WebDevLoop.Core.Queries.RepositoryView>.NotFound("no repo");
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PatchJsonAsync("/api/repos/9", new { isEnabled = false });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(CommandStatus.Succeeded, HttpStatusCode.NoContent)]
    [InlineData(CommandStatus.NotFound, HttpStatusCode.NotFound)]
    [InlineData(CommandStatus.Conflict, HttpStatusCode.Conflict)]
    public async Task removing_a_repository_maps_the_outcome_to_a_status_code(CommandStatus status, HttpStatusCode expected)
    {
        await using var factory = new ApiFactory();
        factory.Registry.RemoveResult = new CommandResult<int>(status, 1, Message: "message");
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.DeleteResponseAsync("/api/repos/1");

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal([1], factory.Registry.Removed);
    }

    [Fact]
    public async Task selecting_a_repository_only_changes_the_ui_context()
    {
        await using var factory = new ApiFactory();
        factory.Repositories.Repositories.Add(ApiData.Repository(3, "gadgets"));
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostJsonAsync("/api/repos/3/select");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, factory.Selection.CurrentRepositoryId);
        Assert.Empty(factory.Enqueuer.Calls);
        Assert.Empty(factory.Registry.Updated);
        Assert.Empty(factory.Registry.Removed);
        Assert.Empty(factory.Registry.Registered);
    }

    [Fact]
    public async Task selecting_works_in_diagnostic_only_mode_and_never_touches_scheduling()
    {
        await using ApiFactory factory = await new ApiFactory().EvaluateAsync(new WebDevLoop.Core.Ports.PrerequisiteCheck("Git", WebDevLoop.Core.Ports.PrerequisiteStatus.Failed, "missing"));
        factory.Repositories.Repositories.Add(ApiData.Repository(3, "gadgets"));
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostJsonAsync("/api/repos/3/select");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(factory.Enqueuer.Calls);
    }

    [Fact]
    public async Task selecting_an_unknown_repository_returns_404_and_keeps_the_current_selection()
    {
        await using var factory = new ApiFactory();
        factory.Selection.Select(1);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostJsonAsync("/api/repos/99/select");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, factory.Selection.CurrentRepositoryId);
    }
}
