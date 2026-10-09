using System.Net;
using System.Text.Json;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Control;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Web.Tests.Api;

public sealed class RunControlEndpointTests
{
    private static readonly PrerequisiteCheck GitMissing = new("Git CLI", PrerequisiteStatus.Failed, "git not found", "Install git.");

    public static TheoryData<string> ControlRoutes => new()
    {
        "/api/spec-runs/run-1/retry",
        "/api/spec-runs/run-1/abort",
        "/api/ticket-runs/t-1/retry",
        "/api/ticket-runs/t-1/skip",
        "/api/ticket-runs/t-1/abort",
    };

    private static async Task<ApiFactory> OperationalAsync()
    {
        ApiFactory factory = await new ApiFactory().EvaluateAsync();
        factory.Runs.SpecRuns.Add(ApiData.SpecRun("run-1", status: SpecRunStatus.NeedsAttention));
        factory.Runs.Tickets.Add(ApiData.TicketRun("t-1"));
        return factory;
    }

    [Fact]
    public async Task retrying_a_spec_run_returns_the_updated_run()
    {
        await using ApiFactory factory = await OperationalAsync();
        factory.Control.OnApplied = (_, id) => factory.Runs.SpecRuns[0] = ApiData.SpecRun(id, status: SpecRunStatus.Running);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostJsonAsync("/api/spec-runs/run-1/retry");
        JsonElement body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Retry", body.GetProperty("action").GetString());
        Assert.Equal(("run-1", "Running"), (body.GetProperty("run").GetProperty("id").GetString(), body.GetProperty("run").GetProperty("status").GetString()));
        Assert.Empty(body.GetProperty("warnings").EnumerateArray());
        Assert.Equal([("retry-spec", "run-1", (SkipDependents?)null)], factory.Control.Calls);
    }

    [Fact]
    public async Task aborting_a_spec_run_returns_the_aborted_run_and_cleanup_warnings()
    {
        await using ApiFactory factory = await OperationalAsync();
        factory.Control.Result = ControlResult.Applied(["Aborting the agent session of step 's-1' failed: gone"]);
        factory.Control.OnApplied = (_, id) => factory.Runs.SpecRuns[0] = ApiData.SpecRun(id, status: SpecRunStatus.Aborted);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostJsonAsync("/api/spec-runs/run-1/abort");
        JsonElement body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Aborted", body.GetProperty("run").GetProperty("status").GetString());
        Assert.Contains("s-1", Assert.Single(body.GetProperty("warnings").EnumerateArray()).GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("retry", "retry-ticket", "Retry")]
    [InlineData("abort", "abort-ticket", "Abort")]
    public async Task ticket_commands_return_the_updated_ticket(string route, string command, string action)
    {
        await using ApiFactory factory = await OperationalAsync();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostJsonAsync($"/api/ticket-runs/t-1/{route}");
        JsonElement body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((action, "t-1"), (body.GetProperty("action").GetString(), body.GetProperty("run").GetProperty("id").GetString()));
        Assert.Equal(command, Assert.Single(factory.Control.Calls).Command);
    }

    [Fact]
    public async Task skipping_a_ticket_without_a_body_unblocks_its_dependents()
    {
        await using ApiFactory factory = await OperationalAsync();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsync("/api/ticket-runs/t-1/skip", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("skip-ticket", "t-1", (SkipDependents?)SkipDependents.Unblock), Assert.Single(factory.Control.Calls));
    }

    [Fact]
    public async Task skipping_a_ticket_can_skip_its_dependents_too()
    {
        await using ApiFactory factory = await OperationalAsync();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostJsonAsync("/api/ticket-runs/t-1/skip", new { dependents = "Skip" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(SkipDependents.Skip, Assert.Single(factory.Control.Calls).Dependents);
    }

    [Fact]
    public async Task an_unknown_skip_policy_is_a_bad_request()
    {
        await using ApiFactory factory = await OperationalAsync();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostJsonAsync("/api/ticket-runs/t-1/skip", new { dependents = "Everything" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Control.Calls);
    }

    [Theory]
    [MemberData(nameof(ControlRoutes))]
    public async Task diagnostic_only_mode_rejects_control_actions_with_the_prerequisite_status(string route)
    {
        await using ApiFactory factory = await new ApiFactory().EvaluateAsync(GitMissing);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostJsonAsync(route);
        JsonElement body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("DiagnosticOnly", body.GetProperty("mode").GetString());
        Assert.Equal("Git CLI", Assert.Single(body.GetProperty("failedChecks").EnumerateArray()).GetProperty("name").GetString());
        Assert.Empty(factory.Control.Calls);
    }

    [Theory]
    [InlineData(ControlOutcome.NotFound, HttpStatusCode.NotFound)]
    [InlineData(ControlOutcome.NotAllowed, HttpStatusCode.Conflict)]
    [InlineData(ControlOutcome.NoActiveSlot, HttpStatusCode.Conflict)]
    [InlineData(ControlOutcome.ConcurrencyConflict, HttpStatusCode.Conflict)]
    public async Task refused_commands_map_to_problem_responses_with_the_reason(ControlOutcome outcome, HttpStatusCode expected)
    {
        await using ApiFactory factory = await OperationalAsync();
        factory.Control.Result = new ControlResult(outcome, "the reason");
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostJsonAsync("/api/spec-runs/run-1/retry");
        JsonElement body = await response.ReadJsonAsync();

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("the reason", body.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("/api/spec-runs/%20/retry")]
    [InlineData("/api/ticket-runs/%20/abort")]
    public async Task malformed_ids_are_not_found_without_calling_the_control_service(string route)
    {
        await using ApiFactory factory = await OperationalAsync();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostJsonAsync(route);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(factory.Control.Calls);
    }
}
