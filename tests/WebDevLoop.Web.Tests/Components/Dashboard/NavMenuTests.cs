using Bunit;
using Microsoft.JSInterop;
using WebDevLoop.Web.Components.Layout;

namespace WebDevLoop.Web.Tests.Components.Dashboard;

public sealed class NavMenuTests : UiTestContext
{
    [Fact]
    public void links_to_every_page_group_including_those_owned_by_other_work_packages()
    {
        IRenderedComponent<NavMenu> cut = Render<NavMenu>();

        string[] hrefs = cut.FindAll("a.nav-link").Select(link => link.GetAttribute("href")!).ToArray();

        Assert.Equal(["/", "/repositories", "/queue", "/settings", "/github", "/health"], hrefs);
    }

    [Fact]
    public void mobile_toggle_opens_and_closes_the_panel_and_reflects_state_in_aria_expanded()
    {
        IRenderedComponent<NavMenu> cut = Render<NavMenu>();
        var toggle = cut.Find("button.app-nav-toggle");

        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
        Assert.DoesNotContain("is-open", cut.Find("#main-nav").ClassList);

        toggle.Click();
        Assert.Equal("true", cut.Find("button.app-nav-toggle").GetAttribute("aria-expanded"));
        Assert.Contains("is-open", cut.Find("#main-nav").ClassList);

        cut.Find("button.app-nav-toggle").Click();
        Assert.Equal("false", cut.Find("button.app-nav-toggle").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void choosing_a_destination_closes_the_mobile_panel()
    {
        IRenderedComponent<NavMenu> cut = Render<NavMenu>();
        cut.Find("button.app-nav-toggle").Click();

        cut.Find("a.nav-link").Click();

        Assert.DoesNotContain("is-open", cut.Find("#main-nav").ClassList);
    }

    [Fact]
    public void embeds_the_current_repository_switcher()
    {
        IRenderedComponent<NavMenu> cut = Render<NavMenu>();

        Assert.NotNull(cut.FindComponent<WebDevLoop.Web.Components.Repositories.RepositorySwitcher>());
    }

    [Fact]
    public void routes_match_the_navigation_entries()
    {
        Assert.Equal("/runs/run-1", UiRoutes.Run("run-1"));
        Assert.Equal("/runs/a%2Fb", UiRoutes.Run("a/b"));
        Assert.Equal(["/", "/repositories", "/queue", "/settings", "/github", "/health"],
            new[] { UiRoutes.Dashboard, UiRoutes.Repositories, UiRoutes.Queue, UiRoutes.Settings, UiRoutes.GitHub, UiRoutes.Health });
    }
}
