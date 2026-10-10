using Bunit;
using WebDevLoop.Web.Components.Layout;

namespace WebDevLoop.Web.Tests.Components.Layout;

public sealed class ReconnectModalTests : BunitContext
{
    [Fact]
    public void exposes_the_element_ids_the_blazor_reconnect_script_depends_on()
    {
        IRenderedComponent<ReconnectModal> cut = Render<ReconnectModal>();

        Assert.NotNull(cut.Find("dialog#components-reconnect-modal"));
        Assert.NotNull(cut.Find("button#components-reconnect-button"));
        Assert.NotNull(cut.Find("button#components-resume-button"));
        Assert.NotNull(cut.Find("button#components-reload-button"));
        Assert.NotNull(cut.Find("span#components-seconds-to-next-attempt"));
    }

    [Theory]
    [InlineData("components-reconnect-button", "components-reconnect-failed-visible", "Try again")]
    [InlineData("components-resume-button", "components-pause-visible", "Resume")]
    [InlineData("components-resume-button", "components-resume-failed-visible", "Resume")]
    [InlineData("components-reload-button", "components-reconnect-rejected-visible", "Reload page")]
    public void each_state_offers_its_action_with_plain_wording(string buttonId, string stateClass, string label)
    {
        IRenderedComponent<ReconnectModal> cut = Render<ReconnectModal>();

        var button = cut.Find($"#{buttonId}");

        Assert.Contains(stateClass, button.ClassList);
        Assert.Equal(label, button.TextContent.Trim());
    }

    [Fact]
    public void every_state_has_a_heading_and_a_message()
    {
        IRenderedComponent<ReconnectModal> cut = Render<ReconnectModal>();

        string[] states =
        [
            "components-reconnect-first-attempt-visible",
            "components-reconnect-repeated-attempt-visible",
            "components-reconnect-failed-visible",
            "components-pause-visible",
            "components-resume-failed-visible",
            "components-reconnect-rejected-visible",
        ];

        foreach (string state in states)
        {
            Assert.NotEmpty(cut.FindAll($"h2.{state}"));
            Assert.NotEmpty(cut.FindAll($"p.{state}"));
        }
    }

    [Fact]
    public void uses_design_system_buttons_and_announces_changes_politely()
    {
        IRenderedComponent<ReconnectModal> cut = Render<ReconnectModal>();

        Assert.Contains("btn-primary", cut.Find("#components-reconnect-button").ClassList);
        Assert.Contains("btn-outline-secondary", cut.Find("#components-reload-button").ClassList);
        Assert.Equal("polite", cut.Find(".reconnect-body").GetAttribute("aria-live"));
        Assert.DoesNotContain("...", cut.Markup);
    }
}
