using Bunit;
using WebDevLoop.Core.Domain;
using WebDevLoop.Web.Components.Runs;
using WebDevLoop.Web.Tests.Components.Support;

namespace WebDevLoop.Web.Tests.Components.Runs;

public sealed class SpecRunDetailTests
{
    private static RunDetailHarness HarnessWithSpec(SpecRunStatus status = SpecRunStatus.Running)
    {
        var harness = new RunDetailHarness();
        harness.Queries.Specs.Add(Views.Spec(status: status, mode: SpecDependencyMode.WaitForMerge));
        return harness;
    }

    [Fact]
    public void Header_shows_spec_status_and_integration_branch()
    {
        using var harness = HarnessWithSpec();

        var cut = harness.Render<SpecRunDetail>(p => p.Add(c => c.Id, "run-1"));

        Assert.Contains("Add billing", cut.Find("h1").TextContent);
        Assert.Equal("Running", cut.Find("[data-testid=spec-status]").TextContent.Trim());
        Assert.Contains("integration/run-1", cut.Find("[data-testid=integration-branch]").TextContent);
        Assert.Contains("#42", cut.Markup);
    }

    [Fact]
    public void Unknown_run_shows_not_found_message()
    {
        using var harness = new RunDetailHarness();

        var cut = harness.Render<SpecRunDetail>(p => p.Add(c => c.Id, "nope"));

        Assert.Contains("not found", cut.Find("[data-testid=not-found]").TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ticket_dag_shows_layers_statuses_and_blockers()
    {
        using var harness = HarnessWithSpec();
        harness.Queries.Tickets.Add(Views.Ticket("t1", 10, TicketRunStatus.Integrated));
        harness.Queries.Tickets.Add(Views.Ticket("t2", 11, TicketRunStatus.Blocked, blockedBy: ["t1", "t3"]));
        harness.Queries.Tickets.Add(Views.Ticket("t3", 12, TicketRunStatus.Implementing));

        var cut = harness.Render<SpecRunDetail>(p => p.Add(c => c.Id, "run-1"));

        Assert.Equal("Integrated", cut.Find("[data-testid=ticket-status-t1]").TextContent.Trim());
        Assert.Equal("Implementing", cut.Find("[data-testid=ticket-status-t3]").TextContent.Trim());
        Assert.Equal("1", cut.Find("[data-testid=ticket-layer-t2]").TextContent.Trim());
        Assert.Contains("#12", cut.Find("[data-testid=ticket-blockers-t2]").TextContent);
        Assert.Equal("/tickets/t2", cut.Find("[data-testid=ticket-link-t2]").GetAttribute("href"));
        Assert.Empty(cut.FindAll("[data-testid=frontier-t2]"));
    }

    [Fact]
    public async Task Frontier_update_event_moves_ticket_from_blocked_to_ready()
    {
        using var harness = HarnessWithSpec();
        harness.Queries.Tickets.Add(Views.Ticket("t1", 10, TicketRunStatus.Implementing));
        harness.Queries.Tickets.Add(Views.Ticket("t2", 11, TicketRunStatus.Blocked, blockedBy: ["t1"]));
        var cut = harness.Render<SpecRunDetail>(p => p.Add(c => c.Id, "run-1"));
        Assert.Equal("Blocked", cut.Find("[data-testid=ticket-status-t2]").TextContent.Trim());

        harness.Queries.Replace(Views.Ticket("t1", 10, TicketRunStatus.Integrated));
        harness.Queries.Replace(Views.Ticket("t2", 11, TicketRunStatus.Ready, blockedBy: ["t1"]));
        await harness.Bus.PublishAsync(Events.TicketStatus("run-1", "t2", TicketRunStatus.Blocked, TicketRunStatus.Ready));

        cut.WaitForAssertion(() => Assert.Equal("Ready", cut.Find("[data-testid=ticket-status-t2]").TextContent.Trim()));
        Assert.NotNull(cut.Find("[data-testid=frontier-t2]"));
    }

    [Fact]
    public async Task Event_of_another_run_does_not_reload_the_page()
    {
        using var harness = HarnessWithSpec();
        harness.Queries.Tickets.Add(Views.Ticket("t1", 10, TicketRunStatus.Blocked));
        var cut = harness.Render<SpecRunDetail>(p => p.Add(c => c.Id, "run-1"));

        harness.Queries.Replace(Views.Ticket("t1", 10, TicketRunStatus.Ready));
        await harness.Bus.PublishAsync(Events.TicketStatus("other-run", "x", TicketRunStatus.Blocked, TicketRunStatus.Ready));

        Assert.Equal("Blocked", cut.Find("[data-testid=ticket-status-t1]").TextContent.Trim());
    }

    [Fact]
    public async Task Integration_saga_checkpoint_appears_without_page_reload()
    {
        using var harness = HarnessWithSpec();
        harness.Queries.Tickets.Add(Views.Ticket("t1", 10, TicketRunStatus.Integrating));
        var saga = harness.Sagas.Start("run-1", "t1");
        var cut = harness.Render<SpecRunDetail>(p => p.Add(c => c.Id, "run-1"));
        Assert.Equal("Started", cut.Find("[data-testid=saga-checkpoint-t1]").TextContent.Trim());

        saga.AdvanceTo(IntegrationSagaCheckpoint.PrCreated, Views.Now);
        await harness.Bus.PublishAsync(new Events.UnmappedEvent());

        cut.WaitForAssertion(() => Assert.Equal("PrCreated", cut.Find("[data-testid=saga-checkpoint-t1]").TextContent.Trim()));
    }

    [Fact]
    public void Stack_layers_list_pull_requests_bottom_to_top()
    {
        using var harness = HarnessWithSpec(SpecRunStatus.AwaitingMerge);
        harness.Queries.Stack.Add(Views.Layer(1, "t1", 201));
        harness.Queries.Stack.Add(Views.Layer(2, "t2", 202));

        var cut = harness.Render<SpecRunDetail>(p => p.Add(c => c.Id, "run-1"));

        var layers = cut.FindAll("[data-testid^=stack-layer-]");
        Assert.Equal(2, layers.Count);
        Assert.Contains("#201", layers[0].TextContent);
        Assert.Contains("#202", layers[1].TextContent);
        Assert.Contains("stack/run-1/t2", layers[1].TextContent);
    }

    [Theory]
    [InlineData(SpecRunStatus.AwaitingMerge, "Awaiting merge")]
    [InlineData(SpecRunStatus.Completed, "Merged")]
    [InlineData(SpecRunStatus.NeedsAttention, "Needs attention")]
    public void Merge_tracking_reflects_spec_status(SpecRunStatus status, string expected)
    {
        using var harness = HarnessWithSpec(status);
        harness.Queries.Stack.Add(Views.Layer(1, "t1", 201));

        var cut = harness.Render<SpecRunDetail>(p => p.Add(c => c.Id, "run-1"));

        Assert.Contains(expected, cut.Find("[data-testid=merge-tracking]").TextContent);
    }

    [Fact]
    public void Audit_events_are_listed_newest_first()
    {
        using var harness = HarnessWithSpec();
        harness.Queries.Events.Add(new(1, "run-1", null, "SpecPrepared", "{}", Views.Now));
        harness.Queries.Events.Add(new(2, "run-1", "t1", "TicketImplemented", "{}", Views.Now.AddMinutes(1)));

        var cut = harness.Render<SpecRunDetail>(p => p.Add(c => c.Id, "run-1"));

        var rows = cut.FindAll("[data-testid=run-event]");
        Assert.Equal(2, rows.Count);
        Assert.Contains("TicketImplemented", rows[0].TextContent);
    }

    [Fact]
    public void Control_actions_are_a_marked_placeholder_without_buttons()
    {
        using var harness = HarnessWithSpec();

        var cut = harness.Render<SpecRunDetail>(p => p.Add(c => c.Id, "run-1"));

        Assert.NotNull(cut.Find("[data-testid=controls-placeholder]"));
        Assert.Empty(cut.FindAll("[data-testid=controls-placeholder] button"));
    }
}
