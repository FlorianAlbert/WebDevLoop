using Bunit;
using WebDevLoop.Core.Domain;
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

        cut.WaitForAssertion(() => Assert.Equal("FixingReviewFindings", cut.Find("[data-testid=ticket-status]").TextContent.Trim()));
        Assert.Equal("3", cut.Find("[data-testid=review-iteration]").TextContent.Trim());
    }

    [Fact]
    public void Controls_area_offers_retry_skip_and_abort_for_the_ticket()
    {
        using var harness = HarnessWithTicket(TicketRunStatus.NeedsAttention);

        var cut = harness.Render<TicketRunDetail>(p => p.Add(c => c.Id, "t1"));

        Assert.Empty(cut.FindAll("[data-testid=controls-placeholder]"));
        Assert.Equal(
            ["control-retry", "control-skip", "control-skip-dependents", "control-abort"],
            cut.FindAll("[data-testid=run-controls] button[data-testid^=control-]").Select(button => button.GetAttribute("data-testid")));
    }
}
