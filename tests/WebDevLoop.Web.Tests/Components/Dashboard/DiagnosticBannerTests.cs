using Bunit;
using WebDevLoop.Web.Components.Dashboard;

namespace WebDevLoop.Web.Tests.Components.Dashboard;

public sealed class DiagnosticBannerTests : UiTestContext
{
    [Fact]
    public void renders_nothing_when_operational()
    {
        IRenderedComponent<DiagnosticBanner> cut = Render<DiagnosticBanner>();

        Assert.Empty(cut.FindAll(".alert"));
    }

    [Fact]
    public async Task warns_that_workflow_actions_are_disabled_in_diagnostic_only_mode()
    {
        await EnterDiagnosticModeAsync();

        IRenderedComponent<DiagnosticBanner> cut = Render<DiagnosticBanner>();

        var alert = cut.Find(".alert");
        Assert.Contains("diagnostic-only", alert.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 prerequisite check failed", alert.TextContent);
        Assert.Equal("/health", alert.QuerySelector("a")!.GetAttribute("href"));
    }
}
