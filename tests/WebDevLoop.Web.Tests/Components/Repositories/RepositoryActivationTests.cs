using Bunit;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Queries;
using WebDevLoop.Infrastructure.GitHub.Auth;
using WebDevLoop.Web.Components.Repositories;
using WebDevLoop.Web.Tests.Api;
using WebDevLoop.Web.Tests.Components.Dashboard;
using WebDevLoop.Web.Tests.GitHubAuth;

namespace WebDevLoop.Web.Tests.Components.Repositories;

public sealed class RepositoryActivationTests : UiTestContext
{
    private readonly StubGitHub _github = new();

    public RepositoryActivationTests()
    {
        Repositories.Repositories.Add(ApiData.Repository(1, "widgets"));
        _github.Installations.Add(new { id = 5, account = new { login = "acme" }, repository_selection = "selected", html_url = "https://github.com/organizations/acme/settings/installations/5", app_slug = "webdevloop" });
        _github.InstallationRepositories[5] = ["acme/widgets", "acme/gadgets", "acme/tools"];
    }

    [Fact]
    public void lists_the_reachable_repositories_and_marks_the_active_ones()
    {
        IRenderedComponent<RepositoriesPage> cut = OpenPicker(signedIn: true);

        Assert.Equal(["acme/gadgets", "acme/tools", "acme/widgets"], Rows(cut).Select(row => row.GetAttribute("data-repo")!));
        Assert.Equal("true", cut.Find("[data-repo='acme/widgets']").GetAttribute("data-active"));
        Assert.Equal("false", cut.Find("[data-repo='acme/tools']").GetAttribute("data-active"));
        Assert.Empty(cut.FindAll("input[name=owner]"));
    }

    [Fact]
    public void filters_the_list_case_insensitively()
    {
        IRenderedComponent<RepositoriesPage> cut = OpenPicker(signedIn: true);

        cut.Find("[data-testid=register-filter]").Input("TOOL");

        Assert.Equal(["acme/tools"], Rows(cut).Select(row => row.GetAttribute("data-repo")!));

        cut.Find("[data-testid=register-filter]").Input("nothing");

        Assert.NotNull(cut.Find("[data-testid=register-no-match]"));
    }

    [Fact]
    public void activating_registers_the_repository_with_defaults_and_selects_it_when_none_is_current()
    {
        Registry.RegisterResult = CommandResult<RepositoryView>.Succeeded(ApiData.Repository(3, "tools"));
        IRenderedComponent<RepositoriesPage> cut = OpenPicker(signedIn: true);

        cut.Find("[data-repo='acme/tools'] [data-testid=activate]").Click();

        cut.WaitForAssertion(() => Assert.Equal(3, Selection.CurrentRepositoryId));
        Assert.Equal(new RegisterRepositoryCommand("acme", "tools"), Assert.Single(Registry.Registered));
        Assert.Equal("true", cut.Find("[data-repo='acme/tools']").GetAttribute("data-active"));
        Assert.NotNull(cut.Find("[data-testid=register-form]"));
    }

    [Fact]
    public void activating_keeps_the_current_repository_when_one_is_selected()
    {
        Selection.Select(1);
        IRenderedComponent<RepositoriesPage> cut = OpenPicker(signedIn: true);

        cut.Find("[data-repo='acme/gadgets'] [data-testid=activate]").Click();

        cut.WaitForAssertion(() => Assert.Single(Registry.Registered));
        Assert.Equal(1, Selection.CurrentRepositoryId);
    }

    [Fact]
    public void shows_why_an_activation_failed_and_keeps_the_repository_inactive()
    {
        Registry.RegisterResult = CommandResult<RepositoryView>.Conflict("acme/tools is already registered");
        IRenderedComponent<RepositoriesPage> cut = OpenPicker(signedIn: true);

        cut.Find("[data-repo='acme/tools'] [data-testid=activate]").Click();

        cut.WaitForAssertion(() => Assert.Contains("already registered", cut.Find("[data-testid=register-errors]").TextContent));
        Assert.Equal("false", cut.Find("[data-repo='acme/tools']").GetAttribute("data-active"));
    }

    [Fact]
    public void removing_needs_confirmation_and_clears_the_selection_when_the_current_repository_goes()
    {
        Selection.Select(1);
        IRenderedComponent<RepositoriesPage> cut = OpenPicker(signedIn: true);

        cut.Find("[data-repo='acme/widgets'] [data-testid=remove]").Click();
        Assert.Empty(Registry.Removed);
        cut.Find("[data-repo='acme/widgets'] [data-testid=confirm-remove]").Click();

        cut.WaitForAssertion(() => Assert.Equal([1], Registry.Removed));
        Assert.Null(Selection.CurrentRepositoryId);
        Assert.Equal("false", cut.Find("[data-repo='acme/widgets']").GetAttribute("data-active"));
    }

    [Fact]
    public void removing_can_be_cancelled()
    {
        IRenderedComponent<RepositoriesPage> cut = OpenPicker(signedIn: true);

        cut.Find("[data-repo='acme/widgets'] [data-testid=remove]").Click();
        cut.Find("[data-repo='acme/widgets'] [data-testid=cancel-remove]").Click();

        Assert.Empty(cut.FindAll("[data-testid=confirm-remove]"));
        Assert.Empty(Registry.Removed);
    }

    [Fact]
    public void explains_why_a_repository_with_history_cannot_be_removed()
    {
        Registry.RemoveResult = CommandResult<int>.Conflict("Repository 1 has spec runs; disable it instead");
        IRenderedComponent<RepositoriesPage> cut = OpenPicker(signedIn: true);

        cut.Find("[data-repo='acme/widgets'] [data-testid=remove]").Click();
        cut.Find("[data-repo='acme/widgets'] [data-testid=confirm-remove]").Click();

        cut.WaitForAssertion(() => Assert.Contains("disable it instead", cut.Find("[data-testid=register-errors]").TextContent));
        Assert.Equal("true", cut.Find("[data-repo='acme/widgets']").GetAttribute("data-active"));
    }

    [Fact]
    public void asks_to_sign_in_on_the_github_page_when_nobody_is_signed_in()
    {
        IRenderedComponent<RepositoriesPage> cut = OpenPicker(signedIn: false);

        Assert.Contains("Sign in with GitHub", cut.Find("[data-testid=register-signin-required]").TextContent);
        Assert.Empty(Rows(cut));
    }

    [Fact]
    public void add_buttons_are_outline_and_name_their_repository()
    {
        IRenderedComponent<RepositoriesPage> cut = OpenPicker(signedIn: true);

        AngleSharp.Dom.IElement add = cut.Find("[data-repo='acme/tools'] [data-testid=activate]");
        Assert.Equal("Add acme/tools", add.GetAttribute("aria-label"));
        Assert.Contains("btn-outline-primary", add.ClassName);
        Assert.NotNull(cut.Find("label[for=register-filter]"));
    }

    [Fact]
    public void the_picker_remove_shows_the_same_consequences_as_the_table()
    {
        IRenderedComponent<RepositoriesPage> cut = OpenPicker(signedIn: true);

        cut.Find("[data-repo='acme/widgets'] [data-testid=remove]").Click();

        Assert.Contains("local clone is deleted", cut.Find("[data-repo='acme/widgets'] [data-testid=remove-confirmation]").TextContent);
    }

    [Fact]
    public void header_toggle_exposes_its_open_state_and_hides_the_empty_state_while_open()
    {
        Repositories.Repositories.Clear();
        IRenderedComponent<RepositoriesPage> cut = OpenPicker(signedIn: true, expectRows: false);

        Assert.Equal("true", cut.Find("[data-testid=show-register]").GetAttribute("aria-expanded"));
        Assert.DoesNotContain("No repositories added", cut.Markup);
        Assert.Contains("Add repository", cut.Find("[data-testid=show-register]").TextContent);
    }

    private IRenderedComponent<RepositoriesPage> OpenPicker(bool signedIn, bool expectRows = true)
    {
        GitHubUserSession session = _github.CreateSession(new MemoryCredentialStore(signedIn ? StubGitHub.SignedIn() : null));
        Services.AddSingleton(session);
        Services.AddSingleton(new GitHubAppAccess(new HttpClient(_github), session, StubGitHub.Options));

        IRenderedComponent<RepositoriesPage> cut = Render<RepositoriesPage>();
        cut.Find("[data-testid=show-register]").Click();
        if (signedIn && expectRows)
        {
            cut.WaitForAssertion(() => Assert.NotEmpty(Rows(cut)));
        }

        return cut;
    }

    private static IReadOnlyList<AngleSharp.Dom.IElement> Rows(IRenderedComponent<RepositoriesPage> cut) =>
        cut.FindAll("[data-testid=register-candidates] li");
}
