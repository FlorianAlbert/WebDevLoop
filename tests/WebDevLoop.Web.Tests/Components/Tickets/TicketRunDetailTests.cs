using Bunit;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Control;
using WebDevLoop.Web.Components.Tickets;
using WebDevLoop.Web.Tests.Components.Support;

namespace WebDevLoop.Web.Tests.Components.Tickets;

public sealed class TicketRunDetailTests
{
    private const string ReviewWithFindings = """
        {"attempt":1,"iteration":1,"reviewed_head":"abc","axis":"coding_standards","verdict":"issues_found","summary":"One problem",
         "findings":[{"axis":"coding_standards","title":"Magic number","file":"A.cs","line":12,"description":"Uses 42","recommendation":"Name it","severity":"major"}]}
        """;

    private static RunDetailHarness HarnessWithTicket(TicketRunStatus status = TicketRunStatus.Reviewing, params string[] blockedBy)
    {
        var harness = new RunDetailHarness();
        harness.Queries.Specs.Add(Views.Spec());
        harness.Queries.Tickets.Add(Views.Ticket("t1", 10, status, reviewIteration: 2, pullRequest: null, blockedBy: blockedBy));
        return harness;
    }

    [Fact]
    public void Header_shows_status_branch_worktree_and_review_iteration()
    {
        using var harness = HarnessWithTicket();

        var cut = harness.Render<TicketRunDetail>(p => p.Add(c => c.Id, "t1"));

        Assert.Contains("Ticket 10", cut.Find("h1").TextContent);
        Assert.Equal("Reviewing", cut.Find("[data-testid=ticket-status]").TextContent.Trim());
        Assert.Contains("ticket/run-1/t1", cut.Find("[data-testid=ticket-branch]").TextContent);
        Assert.Contains("/work/t1", cut.Find("[data-testid=ticket-worktree]").TextContent);
        Assert.Equal("2", cut.Find("[data-testid=review-iteration]").TextContent.Trim());
        Assert.Equal("/runs/run-1", cut.Find("[data-testid=spec-link]").GetAttribute("href"));
    }

    [Fact]
    public void Unknown_ticket_shows_not_found_message()
    {
        using var harness = new RunDetailHarness();

        var cut = harness.Render<TicketRunDetail>(p => p.Add(c => c.Id, "nope"));

        Assert.NotNull(cut.Find("[data-testid=not-found]"));
    }

    [Fact]
    public void Blockers_are_listed_with_their_current_status()
    {
        using var harness = HarnessWithTicket(TicketRunStatus.Blocked, "t0");
        harness.Queries.Tickets.Add(Views.Ticket("t0", 9, TicketRunStatus.Implementing));

        var cut = harness.Render<TicketRunDetail>(p => p.Add(c => c.Id, "t1"));

        string blockers = cut.Find("[data-testid=ticket-blockers]").TextContent;
        Assert.Contains("#9", blockers);
        Assert.Contains("Implementing", blockers);
    }

    [Fact]
    public void Steps_table_shows_role_model_status_attempt_and_finding_count()
    {
        using var harness = HarnessWithTicket();
        harness.Queries.Steps.Add(Views.Step("s1", model: "gpt-recorded", reasoningEffort: "medium"));
        harness.Queries.Steps.Add(Views.Step("s2", StepKind.Review, AgentRole.ReviewerCodingStandards, StepStatus.Succeeded, attempt: 2, resultJson: ReviewWithFindings));

        var cut = harness.Render<TicketRunDetail>(p => p.Add(c => c.Id, "t1"));

        Assert.Contains("Implementer", cut.Find("[data-testid=step-role-s1]").TextContent);
        Assert.Equal("gpt-recorded (medium)", cut.Find("[data-testid=step-model-s1]").TextContent.Trim());
        Assert.Equal("unknown", cut.Find("[data-testid=step-model-s2]").TextContent.Trim());
        Assert.Equal("Succeeded", cut.Find("[data-testid=step-status-s2]").TextContent.Trim());
        Assert.Equal("2", cut.Find("[data-testid=step-attempt-s2]").TextContent.Trim());
        Assert.Equal("1", cut.Find("[data-testid=step-findings-s2]").TextContent.Trim());
        Assert.Equal("/steps/s2", cut.Find("[data-testid=step-link-s2]").GetAttribute("href"));
    }

    [Fact]
    public void Review_findings_are_listed_with_their_location()
    {
        using var harness = HarnessWithTicket();
        harness.Queries.Steps.Add(Views.Step("s2", StepKind.Review, AgentRole.ReviewerCodingStandards, resultJson: ReviewWithFindings));

        var cut = harness.Render<TicketRunDetail>(p => p.Add(c => c.Id, "t1"));

        string finding = cut.Find("[data-testid=finding]").TextContent;
        Assert.Contains("Magic number", finding);
        Assert.Contains("A.cs:12", finding);
        Assert.Contains("major", finding);
    }

    [Fact]
    public async Task Saga_checkpoint_progress_updates_live()
    {
        using var harness = HarnessWithTicket(TicketRunStatus.Integrating);
        harness.Queries.Saga("t1", IntegrationSagaCheckpoint.IntegrationPushed);
        var cut = harness.Render<TicketRunDetail>(p => p.Add(c => c.Id, "t1"));
        Assert.Equal("current", cut.Find("[data-testid=saga-step-IntegrationPushed]").GetAttribute("data-state"));
        Assert.Equal("done", cut.Find("[data-testid=saga-step-Started]").GetAttribute("data-state"));
        Assert.Equal("pending", cut.Find("[data-testid=saga-step-PrCreated]").GetAttribute("data-state"));

        harness.Queries.Saga("t1", IntegrationSagaCheckpoint.PrCreated);
        await harness.Bus.PublishAsync(Events.SagaAdvanced("run-1", "t1", IntegrationSagaCheckpoint.PrCreated));

        cut.WaitForAssertion(() => Assert.Equal("current", cut.Find("[data-testid=saga-step-PrCreated]").GetAttribute("data-state")));
    }

    [Fact]
    public async Task Ticket_status_event_updates_detail_without_reload()
    {
        using var harness = HarnessWithTicket(TicketRunStatus.Reviewing);
        var cut = harness.Render<TicketRunDetail>(p => p.Add(c => c.Id, "t1"));

        harness.Queries.Replace(Views.Ticket("t1", 10, TicketRunStatus.FixingReviewFindings, reviewIteration: 3));
        await harness.Bus.PublishAsync(Events.TicketStatus("run-1", "t1", TicketRunStatus.Reviewing, TicketRunStatus.FixingReviewFindings));

        cut.WaitForAssertion(() => Assert.Equal("Fixing review findings", cut.Find("[data-testid=ticket-status]").TextContent.Trim()));
        Assert.Equal("3", cut.Find("[data-testid=review-iteration]").TextContent.Trim());
    }

    [Fact]
    public void Attention_card_hosts_retry_skip_and_abort_for_a_ticket_that_needs_attention()
    {
        using var harness = HarnessWithTicket(TicketRunStatus.NeedsAttention);

        var cut = harness.Render<TicketRunDetail>(p => p.Add(c => c.Id, "t1"));

        Assert.Empty(cut.FindAll("[data-testid=controls-placeholder]"));
        Assert.Empty(cut.FindAll("[data-testid=run-controls]"));
        Assert.Equal(
            ["control-retry", "control-skip", "control-skip-dependents", "control-abort"],
            cut.FindAll("[data-testid=attention-card] button[data-testid^=control-]").Select(button => button.GetAttribute("data-testid")));
    }

    private static RunDetailHarness HarnessWithDependents()
    {
        var harness = new RunDetailHarness();
        harness.Queries.Specs.Add(Views.Spec());
        harness.Queries.Tickets.Add(Views.Ticket("t1", 10, TicketRunStatus.NeedsAttention) with { Attention = AttentionData.Full() });
        harness.Queries.Tickets.Add(Views.Ticket("t2", 11, TicketRunStatus.Blocked, blockedBy: "t1") with { Title = "Add login" });
        harness.Queries.Tickets.Add(Views.Ticket("t3", 12, TicketRunStatus.Blocked, blockedBy: "t2") with { Title = "Add logout" });
        harness.Queries.Tickets.Add(Views.Ticket("t4", 13, TicketRunStatus.Implementing));
        return harness;
    }

    [Fact]
    public void Needs_attention_replaces_the_red_failure_box_with_the_action_card()
    {
        using var harness = HarnessWithDependents();
        harness.Queries.Replace(harness.Queries.Tickets[0] with { FailureReason = "fatal: raw technical failure" });

        var cut = harness.Render<TicketRunDetail>(p => p.Add(c => c.Id, "t1"));

        Assert.Empty(cut.FindAll(".alert-danger"));
        Assert.Equal(AttentionData.Full().Summary, cut.Find("[data-testid=failure-reason]").TextContent.Trim());
        Assert.DoesNotContain("raw technical failure", cut.Find("[data-testid=attention-card]").TextContent);
        Assert.Equal("Action needed", cut.Find("[data-testid=attention-card] h2").TextContent.Trim());
        Assert.NotNull(cut.Find("[data-testid=attention-steps]"));
    }

    [Fact]
    public void Skip_with_dependents_names_the_tickets_it_also_skips_and_confirms_with_them()
    {
        using var harness = HarnessWithDependents();
        var cut = harness.Render<TicketRunDetail>(p => p.Add(c => c.Id, "t1"));

        Assert.Equal("Also skips #11 Add login, #12 Add logout.", cut.Find("[data-testid=impact-skip-dependents]").TextContent.Trim());
        Assert.Equal("Unblocks #11 Add login.", cut.Find("[data-testid=impact-skip]").TextContent.Trim());
        Assert.Equal("Stay blocked: #11 Add login, #12 Add logout.", cut.Find("[data-testid=impact-abort]").TextContent.Trim());

        cut.Find("[data-testid=control-skip-dependents]").Click();

        string confirmation = cut.Find("[data-testid=control-confirm]").TextContent;
        Assert.Contains("#10 Ticket 10", confirmation);
        Assert.Contains("#11 Add login", confirmation);
        Assert.Contains("#12 Add logout", confirmation);
        Assert.DoesNotContain("Ticket 13", confirmation);
        Assert.Empty(harness.Control.Calls);

        cut.Find("[data-testid=control-confirm-yes]").Click();
        Assert.Equal(("skip-ticket", "t1", (SkipDependents?)SkipDependents.Skip), Assert.Single(harness.Control.Calls));
    }

    [Fact]
    public void Skip_and_abort_confirmations_name_the_affected_tickets()
    {
        using var harness = HarnessWithDependents();
        var cut = harness.Render<TicketRunDetail>(p => p.Add(c => c.Id, "t1"));

        cut.Find("[data-testid=control-skip]").Click();
        Assert.Contains("#11 Add login", cut.Find("[data-testid=control-confirm]").TextContent);
        cut.Find("[data-testid=control-confirm-no]").Click();

        cut.Find("[data-testid=control-abort]").Click();
        Assert.Contains("#12 Add logout", cut.Find("[data-testid=control-confirm]").TextContent);
    }

    [Fact]
    public void A_ticket_without_dependents_says_so()
    {
        using var harness = HarnessWithDependents();
        harness.Queries.Replace(harness.Queries.Tickets[3] with { Status = TicketRunStatus.NeedsAttention, Attention = AttentionData.Full() });

        var cut = harness.Render<TicketRunDetail>(p => p.Add(c => c.Id, "t4"));

        Assert.Equal("No other tickets depend on this one.", cut.Find("[data-testid=impact-skip-dependents]").TextContent.Trim());
    }

    [Fact]
    public void Needs_attention_without_a_structured_reason_falls_back_to_the_generic_guidance()
    {
        using var harness = HarnessWithTicket(TicketRunStatus.NeedsAttention);
        harness.Queries.Replace(harness.Queries.Tickets[0] with { FailureReason = "legacy failure text" });

        var cut = harness.Render<TicketRunDetail>(p => p.Add(c => c.Id, "t1"));

        Assert.Contains("legacy failure text", cut.Find("[data-testid=attention-details-text]").TextContent);
        Assert.Equal("Unclassified", cut.Find("[data-testid=attention-card]").GetAttribute("data-code"));
    }

    [Fact]
    public void Other_statuses_keep_the_controls_area_with_consequence_lines_and_no_card()
    {
        using var harness = HarnessWithTicket(TicketRunStatus.Implementing);

        var cut = harness.Render<TicketRunDetail>(p => p.Add(c => c.Id, "t1"));

        Assert.Empty(cut.FindAll("[data-testid=attention-card]"));
        Assert.NotNull(cut.Find("[data-testid=run-controls] [data-testid=control-abort]"));
        Assert.Equal(4, cut.FindAll("[data-testid=run-controls] .run-controls__consequence").Count);
    }

    [Fact]
    public void An_automatic_fix_in_progress_hides_the_buttons_until_asked()
    {
        using var harness = HarnessWithDependents();
        harness.Queries.Replace(harness.Queries.Tickets[0] with { Attention = AttentionData.Pending() });

        var cut = harness.Render<TicketRunDetail>(p => p.Add(c => c.Id, "t1"));

        Assert.Contains("trying to fix this automatically", cut.Find("[data-testid=attention-auto-fix]").TextContent);
        Assert.False(cut.Find("details[data-testid=attention-actions-anyway]").HasAttribute("open"));
    }
}
