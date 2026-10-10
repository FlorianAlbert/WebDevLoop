using Bunit;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Queries;
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

        Assert.Contains("not found", cut.Find("h1").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not exist", cut.Find("[data-testid=not-found]").TextContent);
        Assert.DoesNotContain("not found", cut.Find("[data-testid=not-found]").TextContent, StringComparison.OrdinalIgnoreCase);
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
        harness.Queries.Saga("t1", IntegrationSagaCheckpoint.Started);
        var cut = harness.Render<SpecRunDetail>(p => p.Add(c => c.Id, "run-1"));
        Assert.Equal("Started", cut.Find("[data-testid=saga-checkpoint-t1]").TextContent.Trim());

        harness.Queries.Saga("t1", IntegrationSagaCheckpoint.PrCreated);
        await harness.Bus.PublishAsync(Events.SagaAdvanced("run-1", "t1", IntegrationSagaCheckpoint.PrCreated));

        cut.WaitForAssertion(() => Assert.Equal("Pr created", cut.Find("[data-testid=saga-checkpoint-t1]").TextContent.Trim()));
    }

    [Fact]
    public void Blocking_specs_are_listed_with_their_status_and_whether_they_are_merged()
    {
        using var harness = HarnessWithSpec();
        harness.Queries.Dependencies["run-1"] =
        [
            new SpecDependencyView("run-0", "acme/widgets#7", 7, "Foundations", SpecRunStatus.AwaitingMerge, false),
            new SpecDependencyView(null, "acme/widgets#5", 5, null, null, false),
            new SpecDependencyView("run-9", "acme/widgets#9", 9, "Done", SpecRunStatus.Completed, true),
        ];

        var cut = harness.Render<SpecRunDetail>(p => p.Add(c => c.Id, "run-1"));

        Assert.Equal("/runs/run-0", cut.Find("[data-testid=spec-blocker-link-run-0]").GetAttribute("href"));
        string waiting = cut.Find("[data-testid=spec-blocker-run-0]").TextContent;
        Assert.Contains("#7", waiting);
        Assert.Contains("Foundations", waiting);
        Assert.Contains("Awaiting merge", waiting);
        Assert.Contains("waiting", waiting, StringComparison.OrdinalIgnoreCase);
        string untracked = cut.Find("[data-testid=spec-blocker-issue-5]").TextContent;
        Assert.Contains("#5", untracked);
        Assert.Contains("not tracked", untracked);
        Assert.Empty(cut.FindAll("[data-testid=spec-blocker-link-issue-5]"));
        Assert.Contains("merged", cut.Find("[data-testid=spec-blocker-run-9]").TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_spec_without_blocking_specs_says_so()
    {
        using var harness = HarnessWithSpec();

        var cut = harness.Render<SpecRunDetail>(p => p.Add(c => c.Id, "run-1"));

        Assert.Contains("Not blocked", cut.Find("[data-testid=spec-blockers]").TextContent);
    }

    [Fact]
    public async Task Status_event_of_a_blocking_spec_refreshes_the_blockers()
    {
        using var harness = HarnessWithSpec();
        harness.Queries.Dependencies["run-1"] = [new SpecDependencyView("run-0", "acme/widgets#7", 7, "Foundations", SpecRunStatus.AwaitingMerge, false)];
        var cut = harness.Render<SpecRunDetail>(p => p.Add(c => c.Id, "run-1"));
        Assert.Contains("Awaiting merge", cut.Find("[data-testid=spec-blocker-run-0]").TextContent);

        harness.Queries.Dependencies["run-1"] = [new SpecDependencyView("run-0", "acme/widgets#7", 7, "Foundations", SpecRunStatus.Completed, true)];
        await harness.Bus.PublishAsync(Events.SpecStatus("run-0", SpecRunStatus.Completed));

        cut.WaitForAssertion(() => Assert.Contains("Completed", cut.Find("[data-testid=spec-blocker-run-0]").TextContent));
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
    public void Progress_events_are_described_in_plain_language()
    {
        using var harness = HarnessWithSpec();
        harness.Queries.Events.Add(new(1, "run-1", null, "SpecRunStatusChanged", "{\"from\":\"Queued\",\"to\":\"Preparing\"}", Views.Now));
        harness.Queries.Events.Add(new(2, "run-1", "t1", "StepRunStatusChanged", "{\"step\":\"s1\",\"to\":\"Running\"}", Views.Now.AddMinutes(1)));
        harness.Queries.Events.Add(new(3, "run-1", "t1", "TicketRunStatusChanged", "{\"from\":\"Ready\",\"to\":\"InReview\"}", Views.Now.AddMinutes(2)));

        var cut = harness.Render<SpecRunDetail>(p => p.Add(c => c.Id, "run-1"));

        var rows = cut.FindAll("[data-testid=run-event]");
        Assert.Contains("Ticket is now in review", rows[0].TextContent);
        Assert.Contains("Step is now running", rows[1].TextContent);
        Assert.Contains("Run is now preparing", rows[2].TextContent);
        Assert.Empty(cut.FindAll("[data-testid=no-events]"));
    }

    [Fact]
    public void Attention_card_hosts_retry_and_abort_for_a_run_that_needs_attention()
    {
        using var harness = HarnessWithSpec(SpecRunStatus.NeedsAttention);

        var cut = harness.Render<SpecRunDetail>(p => p.Add(c => c.Id, "run-1"));

        Assert.Empty(cut.FindAll("[data-testid=controls-placeholder]"));
        Assert.Empty(cut.FindAll("[data-testid=run-controls]"));
        Assert.False(cut.Find("[data-testid=attention-card] [data-testid=control-retry]").HasAttribute("disabled"));
        Assert.NotNull(cut.Find("[data-testid=attention-card] [data-testid=control-abort]"));
    }

    [Fact]
    public void Controls_area_offers_abort_with_its_consequence_while_the_run_is_working()
    {
        using var harness = HarnessWithSpec(SpecRunStatus.Running);
        harness.Queries.Tickets.Add(Views.Ticket("t1", 10, TicketRunStatus.Implementing));

        var cut = harness.Render<SpecRunDetail>(p => p.Add(c => c.Id, "run-1"));

        Assert.Empty(cut.FindAll("[data-testid=attention-card]"));
        Assert.True(cut.Find("[data-testid=run-controls] [data-testid=control-retry]").HasAttribute("disabled"));
        Assert.Contains("1 open ticket", cut.Find("[data-testid=run-controls] [data-testid=consequence-abort]").TextContent);
    }

    [Fact]
    public void Retrying_from_the_detail_page_reloads_the_run()
    {
        using var harness = HarnessWithSpec(SpecRunStatus.NeedsAttention);
        harness.Control.OnApplied = (_, _) => harness.Queries.Specs[0] = Views.Spec(status: SpecRunStatus.Running);
        var cut = harness.Render<SpecRunDetail>(p => p.Add(c => c.Id, "run-1"));

        cut.Find("[data-testid=control-retry]").Click();

        cut.WaitForAssertion(() => Assert.Equal("Running", cut.Find("[data-testid=spec-status]").TextContent.Trim()));
    }

    private static RunDetailHarness HarnessNeedingAttention(AttentionReason? attention = null, string? failure = "fatal: raw technical failure")
    {
        var harness = new RunDetailHarness();
        harness.Queries.Specs.Add(Views.Spec(status: SpecRunStatus.NeedsAttention) with { Attention = attention ?? AttentionData.RunReason(), FailureReason = failure });
        harness.Queries.Tickets.Add(Views.Ticket("t1", 10, TicketRunStatus.Implementing));
        harness.Queries.Tickets.Add(Views.Ticket("t2", 11, TicketRunStatus.Blocked, blockedBy: "t1"));
        harness.Queries.Tickets.Add(Views.Ticket("t3", 12, TicketRunStatus.Integrated));
        return harness;
    }

    [Fact]
    public void Needs_attention_shows_the_action_card_instead_of_the_red_failure_box()
    {
        using var harness = HarnessNeedingAttention();

        var cut = harness.Render<SpecRunDetail>(p => p.Add(c => c.Id, "run-1"));

        Assert.Empty(cut.FindAll(".alert-danger"));
        Assert.Equal(AttentionData.RunReason().Summary, cut.Find("[data-testid=failure-reason]").TextContent.Trim());
        Assert.Equal("Your decision", cut.Find("[data-testid=attention-cause]").TextContent.Trim());
        Assert.DoesNotContain("raw technical failure", cut.Find("[data-testid=attention-card]").TextContent);
        Assert.False(cut.Find("[data-testid=attention-details]").HasAttribute("open"));
    }

    [Fact]
    public void Abort_run_names_the_number_of_open_tickets_in_its_consequence_and_confirmation()
    {
        using var harness = HarnessNeedingAttention();
        var cut = harness.Render<SpecRunDetail>(p => p.Add(c => c.Id, "run-1"));

        Assert.Equal("Aborts 2 open tickets (#10 Ticket 10, #11 Ticket 11).", cut.Find("[data-testid=impact-abort]").TextContent.Trim());
        Assert.Contains("Starts another review round.", cut.Find("[data-testid=consequence-retry]").TextContent);

        cut.Find("[data-testid=control-abort]").Click();

        Assert.Contains("2 open tickets", cut.Find("[data-testid=control-confirm]").TextContent);
        Assert.Empty(harness.Control.Calls);
        cut.Find("[data-testid=control-confirm-yes]").Click();
        Assert.Equal("abort-spec", Assert.Single(harness.Control.Calls).Command);
    }

    [Fact]
    public void Merge_tracking_shows_the_real_summary_and_never_the_literal_parameter_expression()
    {
        using var harness = HarnessNeedingAttention(failure: "closed unmerged");
        harness.Queries.Stack.Add(Views.Layer(1, "t1", 201));

        var cut = harness.Render<SpecRunDetail>(p => p.Add(c => c.Id, "run-1"));

        string text = cut.Find("[data-testid=merge-tracking]").TextContent;
        Assert.Contains("Needs attention: " + AttentionData.RunReason().Summary.TrimEnd('.'), text);
        Assert.DoesNotContain("_spec.FailureReason", text);
        Assert.DoesNotContain("_spec.FailureReason", cut.Markup);
    }

    [Fact]
    public void Merge_tracking_passes_the_failure_text_as_a_value_when_there_is_no_structured_reason()
    {
        using var harness = new RunDetailHarness();
        harness.Queries.Specs.Add(Views.Spec(status: SpecRunStatus.NeedsAttention) with { FailureReason = "closed unmerged" });

        var cut = harness.Render<SpecRunDetail>(p => p.Add(c => c.Id, "run-1"));

        Assert.DoesNotContain("_spec.FailureReason", cut.Find("[data-testid=merge-tracking]").TextContent);
        Assert.Contains("Needs attention: ", cut.Find("[data-testid=merge-tracking]").TextContent);
    }

    [Fact]
    public void Merge_tracking_panel_prefers_the_summary_over_the_raw_failure()
    {
        using var context = new BunitContext();

        var withReason = context.Render<MergeTrackingPanel>(p => p
            .Add(c => c.Status, SpecRunStatus.NeedsAttention)
            .Add(c => c.Attention, AttentionData.RunReason())
            .Add(c => c.FailureReason, "raw"));
        var withFailure = context.Render<MergeTrackingPanel>(p => p
            .Add(c => c.Status, SpecRunStatus.NeedsAttention)
            .Add(c => c.FailureReason, "raw failure."));
        var withNothing = context.Render<MergeTrackingPanel>(p => p.Add(c => c.Status, SpecRunStatus.NeedsAttention));

        Assert.Equal("Needs attention: The parent review still finds problems after 3 rounds.", withReason.Find("p").TextContent.Trim());
        Assert.Equal("Needs attention: raw failure.", withFailure.Find("p").TextContent.Trim());
        Assert.Contains("merge tracking failed", withNothing.Find("p").TextContent);
    }

    [Fact]
    public void Attention_events_are_described_in_plain_language_on_the_timeline()
    {
        using var harness = HarnessWithSpec();
        harness.Queries.Events.Add(new(1, "run-1", "t1", "AttentionRaised", "{\"code\":\"WorktreeNotClean\",\"summary\":\"The working folder is dirty.\",\"cause\":\"WebDevLoop\"}", Views.Now));
        harness.Queries.Events.Add(new(2, "run-1", "t1", "AttentionAutoResolved", "{\"code\":\"WorktreeNotClean\",\"stage\":\"Known\",\"summary\":\"Removed 3 untracked files\",\"resume\":\"Retry\"}", Views.Now.AddMinutes(1)));
        harness.Queries.Events.Add(new(3, "run-1", "t1", "ControlAutoRetry", "{\"action\":\"AutoRetry\",\"status\":\"Implementing\",\"tickets\":[]}", Views.Now.AddMinutes(2)));

        var cut = harness.Render<SpecRunDetail>(p => p.Add(c => c.Id, "run-1"));

        string[] rows = cut.FindAll("[data-testid=run-event]").Select(row => row.TextContent).ToArray();
        Assert.Contains("WebDevLoop retried it automatically", rows[0]);
        Assert.Contains("WebDevLoop fixed it by itself: Removed 3 untracked files and resumed the work", rows[1]);
        Assert.Contains("Needs attention: The working folder is dirty", rows[2]);
    }
}
