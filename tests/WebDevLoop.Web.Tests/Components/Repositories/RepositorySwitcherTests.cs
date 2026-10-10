using Bunit;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Web.Components.Repositories;
using WebDevLoop.Web.Tests.Api;
using WebDevLoop.Web.Tests.Components.Dashboard;

namespace WebDevLoop.Web.Tests.Components.Repositories;

public sealed class RepositorySwitcherTests : UiTestContext
{
    public RepositorySwitcherTests()
    {
        Repositories.Repositories.Add(ApiData.Repository(1, "widgets"));
        Repositories.Repositories.Add(ApiData.Repository(2, "gadgets"));
        Repositories.Repositories.Add(ApiData.Repository(3, "legacy", enabled: false));
    }

    [Fact]
    public void offers_enabled_repositories_and_preselects_the_current_one()
    {
        Selection.Select(2);

        IRenderedComponent<RepositorySwitcher> cut = Render<RepositorySwitcher>();

        string[] options = cut.FindAll("select option").Select(option => option.TextContent.Trim()).ToArray();
        Assert.Equal(["No repository selected", "acme/widgets", "acme/gadgets"], options);
        Assert.Equal("2", cut.Find("select").GetAttribute("value"));
    }

    [Fact]
    public void choosing_a_repository_switches_the_ui_context_only()
    {
        IRenderedComponent<RepositorySwitcher> cut = Render<RepositorySwitcher>();

        cut.Find("select").Change("2");

        Assert.Equal(2, Selection.CurrentRepositoryId);
        Assert.Empty(Enqueuer.Calls);
        Assert.Empty(Registry.Updated);
    }

    [Fact]
    public void choosing_the_placeholder_clears_the_selection()
    {
        Selection.Select(1);
        IRenderedComponent<RepositorySwitcher> cut = Render<RepositorySwitcher>();

        cut.Find("select").Change(string.Empty);

        Assert.Null(Selection.CurrentRepositoryId);
    }

    [Fact]
    public void follows_selection_and_repository_list_changes_made_elsewhere()
    {
        IRenderedComponent<RepositorySwitcher> cut = Render<RepositorySwitcher>();
        Repositories.Repositories.Add(ApiData.Repository(4, "tools"));

        var context = Services.GetRequiredService<RepositoryContext>();
        context.Select(4);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("acme/tools", cut.Markup);
            Assert.Equal("4", cut.Find("select").GetAttribute("value"));
        });
    }

    [Fact]
    public void shows_a_register_hint_without_repositories()
    {
        Repositories.Repositories.Clear();

        IRenderedComponent<RepositorySwitcher> cut = Render<RepositorySwitcher>();

        Assert.Empty(cut.FindAll("select"));
        Assert.Equal("/repositories", cut.Find("a").GetAttribute("href"));
    }

    [Fact]
    public void a_current_repository_that_no_longer_exists_is_cleared()
    {
        Selection.Select(4);

        IRenderedComponent<RepositorySwitcher> cut = Render<RepositorySwitcher>();

        cut.WaitForAssertion(() =>
        {
            Assert.Null(Selection.CurrentRepositoryId);
            Assert.Equal(string.Empty, cut.Find("select").GetAttribute("value") ?? string.Empty);
        });
    }

    [Fact]
    public void a_disabled_but_existing_current_repository_is_kept()
    {
        Selection.Select(3);

        Render<RepositorySwitcher>();

        Assert.Equal(3, Selection.CurrentRepositoryId);
    }
}
