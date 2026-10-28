using Bunit;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Control;
using WebDevLoop.Web.Components.Tickets;
using WebDevLoop.Web.Tests.Components.Support;

namespace WebDevLoop.Web.Tests.Components.Tickets;

public sealed class TicketRunControlsTests
{
    private static IRenderedComponent<TicketRunControls> Render(RunDetailHarness harness, TicketRunStatus status) =>
        harness.Render<TicketRunControls>(p => p.Add(c => c.Ticket, Views.Ticket("t1", 10, status)));

    [Fact]
    public void Retrying_a_ticket_that_needs_attention_needs_no_confirmation()
    {
        using var harness = new RunDetailHarness();
        var cut = Render(harness, TicketRunStatus.NeedsAttention);

        cut.Find("[data-testid=control-retry]").Click();

        Assert.Equal([("retry-ticket", "t1")], harness.Control.Calls);
    }

    [Theory]
    [InlineData(TicketRunStatus.Blocked)]
    [InlineData(TicketRunStatus.Ready)]
    [InlineData(TicketRunStatus.NeedsAttention)]
    public void Manual_skip_controls_are_absent(TicketRunStatus status)
    {
        using var harness = new RunDetailHarness();
        var cut = Render(harness, status);

        Assert.Empty(cut.FindAll("[data-testid=control-skip], [data-testid=control-skip-dependents]"));
    }

    [Fact]
    public void Aborting_a_ticket_is_confirmed()
    {
        using var harness = new RunDetailHarness();
        var cut = Render(harness, TicketRunStatus.Implementing);

        cut.Find("[data-testid=control-abort]").Click();
        cut.Find("[data-testid=control-confirm-yes]").Click();

        Assert.Equal("abort-ticket", Assert.Single(harness.Control.Calls).Command);
    }

    [Fact]
    public void A_ticket_being_implemented_can_only_be_aborted()
    {
        using var harness = new RunDetailHarness();

        var cut = Render(harness, TicketRunStatus.Implementing);

        Assert.Equal(
            [true, false],
            new[] { "retry", "abort" }.Select(key => cut.Find($"[data-testid=control-{key}]").HasAttribute("disabled")));
    }

    [Fact]
    public void A_blocked_ticket_can_be_aborted_but_not_retried()
    {
        using var harness = new RunDetailHarness();

        var cut = Render(harness, TicketRunStatus.Blocked);

        Assert.Equal(
            [true, false],
            new[] { "retry", "abort" }.Select(key => cut.Find($"[data-testid=control-{key}]").HasAttribute("disabled")));
    }

    [Fact]
    public async Task Diagnostic_only_mode_disables_ticket_controls()
    {
        using var harness = new RunDetailHarness();
        await harness.EnterDiagnosticModeAsync();

        var cut = Render(harness, TicketRunStatus.NeedsAttention);

        Assert.Equal([true, true], cut.FindAll("button[data-testid^=control-]").Select(button => button.HasAttribute("disabled")));
    }
}
