using Bunit;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Control;
using WebDevLoop.Core.Queries;
using WebDevLoop.Web.Components.Runs;
using WebDevLoop.Web.Tests.Components.Support;

namespace WebDevLoop.Web.Tests.Components.Runs;

public sealed class SpecRunControlsTests
{
    private static IRenderedComponent<SpecRunControls> Render(RunDetailHarness harness, SpecRunStatus status, Action? onChanged = null) =>
        harness.Render<SpecRunControls>(p => p
            .Add(c => c.Spec, Views.Spec(status: status))
            .Add(c => c.OnChanged, () => onChanged?.Invoke()));

    [Fact]
    public void Retrying_a_spec_that_needs_attention_calls_the_service_and_reports_the_result()
    {
        using var harness = new RunDetailHarness();
        int changed = 0;
        var cut = Render(harness, SpecRunStatus.NeedsAttention, () => changed++);

        cut.Find("[data-testid=control-retry]").Click();

        Assert.Equal([("retry-spec", "run-1", (SkipDependents?)null)], harness.Control.Calls);
        Assert.Contains("Retry", cut.Find("[data-testid=control-result]").TextContent, StringComparison.Ordinal);
        Assert.Contains("alert-success", cut.Find("[data-testid=control-result]").ClassName, StringComparison.Ordinal);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Retry_is_only_offered_for_a_spec_that_needs_attention()
    {
        using var harness = new RunDetailHarness();

        var cut = Render(harness, SpecRunStatus.Running);

        Assert.True(cut.Find("[data-testid=control-retry]").HasAttribute("disabled"));
        Assert.False(cut.Find("[data-testid=control-abort]").HasAttribute("disabled"));
    }

    [Fact]
    public void Aborting_asks_for_confirmation_before_calling_the_service()
    {
        using var harness = new RunDetailHarness();
        var cut = Render(harness, SpecRunStatus.Running);

        cut.Find("[data-testid=control-abort]").Click();

        Assert.Empty(harness.Control.Calls);
        Assert.Contains("cannot be undone", cut.Find("[data-testid=control-confirm]").TextContent, StringComparison.Ordinal);

        cut.Find("[data-testid=control-confirm-yes]").Click();

        Assert.Equal("abort-spec", Assert.Single(harness.Control.Calls).Command);
        Assert.Empty(cut.FindAll("[data-testid=control-confirm]"));
    }

    [Fact]
    public void Cancelling_the_confirmation_changes_nothing()
    {
        using var harness = new RunDetailHarness();
        var cut = Render(harness, SpecRunStatus.Running);

        cut.Find("[data-testid=control-abort]").Click();
        cut.Find("[data-testid=control-confirm-no]").Click();

        Assert.Empty(harness.Control.Calls);
        Assert.Empty(cut.FindAll("[data-testid=control-confirm]"));
    }

    [Theory]
    [InlineData(SpecRunStatus.Completed)]
    [InlineData(SpecRunStatus.Aborted)]
    public void A_finished_spec_offers_no_control(SpecRunStatus finished)
    {
        using var harness = new RunDetailHarness();

        var cut = Render(harness, finished);

        Assert.Equal([true, true], cut.FindAll("button[data-testid^=control-]").Select(button => button.HasAttribute("disabled")));
    }

    [Fact]
    public async Task Diagnostic_only_mode_disables_every_control_and_says_why()
    {
        using var harness = new RunDetailHarness();
        await harness.EnterDiagnosticModeAsync();

        var cut = Render(harness, SpecRunStatus.NeedsAttention);

        Assert.Equal([true, true], cut.FindAll("button[data-testid^=control-]").Select(button => button.HasAttribute("disabled")));
        Assert.Contains("diagnostic-only", cut.Find("[data-testid=controls-disabled]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refused_command_shows_the_reason()
    {
        using var harness = new RunDetailHarness();
        harness.Control.Result = new ControlResult(ControlOutcome.NoActiveSlot, "Every active-spec slot of repository 1 is taken.");
        int changed = 0;
        var cut = Render(harness, SpecRunStatus.NeedsAttention, () => changed++);

        cut.Find("[data-testid=control-retry]").Click();

        var result = cut.Find("[data-testid=control-result]");
        Assert.Contains("slot of repository 1 is taken", result.TextContent, StringComparison.Ordinal);
        Assert.Contains("alert-warning", result.ClassName, StringComparison.Ordinal);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void Cleanup_warnings_of_an_applied_command_are_listed()
    {
        using var harness = new RunDetailHarness();
        harness.Control.Result = ControlResult.Applied(["Stopping the tester application on port 41003 failed: denied"]);
        var cut = Render(harness, SpecRunStatus.Running);

        cut.Find("[data-testid=control-abort]").Click();
        cut.Find("[data-testid=control-confirm-yes]").Click();

        Assert.Contains("port 41003", cut.Find("[data-testid=control-warnings]").TextContent, StringComparison.Ordinal);
    }
}
