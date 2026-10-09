using Bunit;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Queries;
using WebDevLoop.Web.Components.Steps;
using WebDevLoop.Web.Tests.Components.Support;

namespace WebDevLoop.Web.Tests.Components.Steps;

public sealed class StepRunDetailTests
{
    private const string ReviewJson = """
        {"attempt":1,"iteration":2,"reviewed_head":"abc","axis":"specification","verdict":"issues_found","summary":"Spec gaps",
         "findings":[{"axis":"specification","title":"Missing empty case","file":"B.cs","line":7,"description":"No empty input","recommendation":"Handle it","kind":"missing"}]}
        """;

    private static RunDetailHarness HarnessWithStep(StepRunView step)
    {
        var harness = new RunDetailHarness();
        harness.Queries.Specs.Add(Views.Spec());
        harness.Queries.Tickets.Add(Views.Ticket("t1", 10, TicketRunStatus.Reviewing));
        harness.Queries.Steps.Add(step);
        return harness;
    }

    private static IRenderedComponent<StepRunDetail> Render(RunDetailHarness harness) =>
        harness.Render<StepRunDetail>(p => p.Add(c => c.Id, "s1").Add(c => c.LogPollInterval, TimeSpan.Zero));

    [Fact]
    public void Header_shows_role_model_status_session_and_links()
    {
        using var harness = HarnessWithStep(Views.Step("s1", StepKind.Review, AgentRole.ReviewerSpecification, StepStatus.Running));

        var cut = Render(harness);

        Assert.Contains("Review", cut.Find("h1").TextContent);
        Assert.Equal("Running", cut.Find("[data-testid=step-status]").TextContent.Trim());
        Assert.Contains("ReviewerSpecification", cut.Find("[data-testid=step-role]").TextContent);
        Assert.Contains("model-ReviewerSpecification", cut.Find("[data-testid=step-model]").TextContent);
        Assert.Contains("copilot-session-1", cut.Find("[data-testid=copilot-session]").TextContent);
        Assert.Equal("/tickets/t1", cut.Find("[data-testid=ticket-link]").GetAttribute("href"));
        Assert.Equal("/runs/run-1", cut.Find("[data-testid=spec-link]").GetAttribute("href"));
    }

    [Fact]
    public void Unknown_step_shows_not_found_message()
    {
        using var harness = new RunDetailHarness();

        var cut = Render(harness);

        Assert.NotNull(cut.Find("[data-testid=not-found]"));
    }

    [Fact]
    public void Step_detail_displays_role_policy()
    {
        using var harness = HarnessWithStep(Views.Step("s1"));

        var cut = Render(harness);

        string policy = cut.Find("[data-testid=role-policy]").TextContent;
        Assert.Contains("CreateLocalCommit", policy);
        Assert.Contains("git push", policy);
        Assert.Contains("report_implementation", policy);
    }

    [Fact]
    public void Step_detail_displays_structured_report()
    {
        using var harness = HarnessWithStep(Views.Step("s1", StepKind.Review, AgentRole.ReviewerSpecification, resultJson: ReviewJson));

        var cut = Render(harness);

        string report = cut.Find("[data-testid=structured-report]").TextContent;
        Assert.Contains("Spec gaps", report);
        Assert.Contains("issues_found", report);
        Assert.Contains("Missing empty case", report);
        Assert.Contains("B.cs:7", report);
        Assert.Contains("\"reviewed_head\": \"abc\"", cut.Find("[data-testid=report-json]").TextContent);
    }

    [Fact]
    public void Step_without_result_says_no_report_yet()
    {
        using var harness = HarnessWithStep(Views.Step("s1", status: StepStatus.Running));

        var cut = Render(harness);

        Assert.NotNull(cut.Find("[data-testid=no-report]"));
    }

    [Fact]
    public void Step_without_agent_role_has_no_policy_panel()
    {
        using var harness = HarnessWithStep(Views.Step("s1", StepKind.ParentReview, role: null));

        var cut = Render(harness);

        Assert.Empty(cut.FindAll("[data-testid=role-policy]"));
    }

    [Fact]
    public void Live_log_tail_shows_agent_output()
    {
        using var harness = HarnessWithStep(Views.Step("s1", status: StepStatus.Running));
        harness.Logs.Append("Editing Program.cs");

        var cut = Render(harness);

        Assert.Contains("Editing Program.cs", cut.Find("[data-testid=log-entry]").TextContent);
    }

    [Fact]
    public async Task Step_status_event_refreshes_status_and_report_without_reload()
    {
        using var harness = HarnessWithStep(Views.Step("s1", StepKind.Review, AgentRole.ReviewerSpecification, StepStatus.Running));
        var cut = Render(harness);
        Assert.NotEmpty(cut.FindAll("[data-testid=no-report]"));

        harness.Queries.Steps[0] = Views.Step("s1", StepKind.Review, AgentRole.ReviewerSpecification, StepStatus.Succeeded, resultJson: ReviewJson);
        await harness.Bus.PublishAsync(Events.StepStatus("run-1", "t1", "s1", StepStatus.Succeeded));

        cut.WaitForAssertion(() => Assert.Equal("Succeeded", cut.Find("[data-testid=step-status]").TextContent.Trim()));
        Assert.Contains("Spec gaps", cut.Find("[data-testid=structured-report]").TextContent);
    }

    [Fact]
    public void Control_actions_are_a_marked_placeholder()
    {
        using var harness = HarnessWithStep(Views.Step("s1"));

        var cut = Render(harness);

        Assert.NotNull(cut.Find("[data-testid=controls-placeholder]"));
        Assert.Empty(cut.FindAll("[data-testid=controls-placeholder] button"));
    }
}
