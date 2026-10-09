using Bunit;
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
