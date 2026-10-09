using System.Net;
using System.Text.Json;
using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Web.Tests.Api;

public sealed class RunStateEndpointTests
{
    private static ApiFactory SeededFactory()
    {
        var factory = new ApiFactory();
        factory.Runs.SpecRuns.Add(ApiData.SpecRun("run-1"));
        factory.Runs.Tickets.AddRange([ApiData.TicketRun("t-1"), ApiData.TicketRun("t-2", "run-1", "t-1")]);
        factory.Runs.Steps.AddRange([ApiData.Step("s-1", "t-1"), ApiData.Step("s-2", "t-1")]);
        factory.Runs.Events.Add(new RunEventView(1, "run-1", null, "SpecRunStatusChanged", "{}", ApiData.Now));
        factory.Runs.Stack.Add(new StackLayerView(1, "t-1", "branch", "main", new string('a', 40), 7, null, true, null));
        return factory;
    }

    [Fact]
    public async Task a_spec_run_is_returned_with_status_and_integration_branch()
    {
        await using ApiFactory factory = SeededFactory();
        using HttpClient client = factory.CreateClient();

        JsonElement run = await client.GetJsonAsync("/api/spec-runs/run-1");

        Assert.Equal(("run-1", "Queued", "webdevloop/run-1/integration", 10), (run.GetProperty("id").GetString(), run.GetProperty("status").GetString(), run.GetProperty("integrationBranch").GetString(), run.GetProperty("parentIssueNumber").GetInt32()));
    }

    [Theory]
    [InlineData("/api/spec-runs/missing")]
    [InlineData("/api/spec-runs/bad id!")]
    [InlineData("/api/spec-runs/missing/tickets")]
    [InlineData("/api/spec-runs/missing/events")]
    [InlineData("/api/spec-runs/missing/stack")]
    [InlineData("/api/ticket-runs/missing")]
    [InlineData("/api/ticket-runs/missing/steps")]
    [InlineData("/api/steps/missing")]
    [InlineData("/api/steps/missing/logs")]
    public async Task unknown_runs_return_404_with_a_problem_body(string url)
    {
        await using ApiFactory factory = SeededFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetResponseAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(404, (await response.ReadJsonAsync()).GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task tickets_of_a_spec_run_show_status_and_blockers()
    {
        await using ApiFactory factory = SeededFactory();
        using HttpClient client = factory.CreateClient();

        JsonElement tickets = await client.GetJsonAsync("/api/spec-runs/run-1/tickets");

        Assert.Equal(["t-1", "t-2"], tickets.EnumerateArray().Select(ticket => ticket.GetProperty("id").GetString()));
        Assert.Equal("Ready", tickets[0].GetProperty("status").GetString());
        Assert.Equal(["t-1"], tickets[1].GetProperty("blockedByTicketRunIds").EnumerateArray().Select(id => id.GetString()));
    }

    [Fact]
    public async Task events_and_stack_of_a_spec_run_are_listed()
    {
        await using ApiFactory factory = SeededFactory();
        using HttpClient client = factory.CreateClient();

        JsonElement events = await client.GetJsonAsync("/api/spec-runs/run-1/events");
        JsonElement stack = await client.GetJsonAsync("/api/spec-runs/run-1/stack");

        Assert.Equal("SpecRunStatusChanged", Assert.Single(events.EnumerateArray()).GetProperty("type").GetString());
        Assert.Equal((1, 7, true), (stack[0].GetProperty("position").GetInt32(), stack[0].GetProperty("pullRequestNumber").GetInt32(), stack[0].GetProperty("isDraft").GetBoolean()));
    }

    [Fact]
    public async Task a_ticket_run_and_its_steps_are_returned()
    {
        await using ApiFactory factory = SeededFactory();
        using HttpClient client = factory.CreateClient();

        JsonElement ticket = await client.GetJsonAsync("/api/ticket-runs/t-2");
        JsonElement steps = await client.GetJsonAsync("/api/ticket-runs/t-1/steps");

        Assert.Equal(("t-2", "run-1"), (ticket.GetProperty("id").GetString(), ticket.GetProperty("specRunId").GetString()));
        Assert.Equal(["s-1", "s-2"], steps.EnumerateArray().Select(step => step.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task a_step_exposes_status_role_session_and_timeout()
    {
        await using ApiFactory factory = SeededFactory();
        using HttpClient client = factory.CreateClient();

        JsonElement step = await client.GetJsonAsync("/api/steps/s-1");

        Assert.Equal(("Implement", "Implementer", "Running", "copilot-1"), (step.GetProperty("kind").GetString(), step.GetProperty("agentRole").GetString(), step.GetProperty("status").GetString(), step.GetProperty("copilotSessionId").GetString()));
        Assert.True(step.TryGetProperty("timeoutAt", out _));
    }

    [Fact]
    public async Task agent_logs_are_returned_after_the_requested_sequence()
    {
        await using ApiFactory factory = SeededFactory();
        factory.Logs.Entries.AddRange(
        [
            new AgentLogView(1, ApiData.Now, AgentLogKind.Assistant, "hello"),
            new AgentLogView(2, ApiData.Now, AgentLogKind.ToolStarted, "run tests"),
            new AgentLogView(3, ApiData.Now, AgentLogKind.Error, "boom"),
        ]);
        using HttpClient client = factory.CreateClient();

        JsonElement all = await client.GetJsonAsync("/api/steps/s-1/logs");
        JsonElement newer = await client.GetJsonAsync("/api/steps/s-1/logs?after=1");
        JsonElement none = await client.GetJsonAsync("/api/steps/s-1/logs?after=3");

        Assert.Equal(3, all.GetProperty("entries").GetArrayLength());
        Assert.Equal(("ToolStarted", "run tests"), (newer.GetProperty("entries")[0].GetProperty("kind").GetString(), newer.GetProperty("entries")[0].GetProperty("text").GetString()));
        Assert.Equal((3, 2), (all.GetProperty("lastSequence").GetInt32(), newer.GetProperty("entries").GetArrayLength()));
        Assert.Equal((0, 3), (none.GetProperty("entries").GetArrayLength(), none.GetProperty("lastSequence").GetInt32()));
        Assert.Equal("s-1", all.GetProperty("stepRunId").GetString());
    }

    [Fact]
    public async Task a_negative_log_cursor_is_treated_as_the_start()
    {
        await using ApiFactory factory = SeededFactory();
        factory.Logs.Entries.Add(new AgentLogView(1, ApiData.Now, AgentLogKind.Assistant, "hello"));
        using HttpClient client = factory.CreateClient();

        JsonElement logs = await client.GetJsonAsync("/api/steps/s-1/logs?after=-5");

        Assert.Equal(1, logs.GetProperty("entries").GetArrayLength());
        Assert.Equal(0, factory.Logs.LastAfterSequence);
    }
}
