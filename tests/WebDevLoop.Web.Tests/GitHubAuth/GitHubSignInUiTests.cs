using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Queries;
using WebDevLoop.Infrastructure.GitHub.Auth;
using WebDevLoop.Web.Components.GitHub;
using WebDevLoop.Web.Tests.Api;

namespace WebDevLoop.Web.Tests.GitHubAuth;

public sealed class GitHubSignInUiTests : BunitContext
{
    private readonly StubGitHub _github = new();
    private readonly FakeRepositoryQueries _repositories = new();

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    [Fact]
    public void banner_offers_the_sign_in_when_nobody_is_signed_in_and_returns_to_the_current_page()
    {
        Use(_github.CreateSession());
        NavigationManager navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/queue");

        IRenderedComponent<GitHubSignInBanner> banner = Render<GitHubSignInBanner>();
        Assert.Contains("You are not signed in to GitHub.", banner.Find("[data-testid=github-signin-banner]").TextContent);

        banner.Find("[data-testid=github-signin]").Click();

        Assert.Equal("http://localhost/auth/github/login?returnUrl=%2Fqueue", navigation.Uri);
    }

    [Fact]
    public async Task banner_disappears_once_the_user_signed_in()
    {
        GitHubUserSession session = Use(_github.CreateSession());
        IRenderedComponent<GitHubSignInBanner> banner = Render<GitHubSignInBanner>();

        await session.CompleteSignInAsync("code", new Uri("http://localhost/auth/github/callback"), "verifier", Ct);

        banner.WaitForAssertion(() => Assert.Empty(banner.FindAll("[data-testid=github-signin-banner]")));
    }

    [Fact]
    public void banner_explains_a_missing_configuration()
    {
        Use(new GitHubUserSession(new HttpClient(_github), new FakeClock(), new GitHubAuthOptions(), new MemoryCredentialStore()));

        IRenderedComponent<GitHubSignInBanner> banner = Render<GitHubSignInBanner>();

        Assert.Contains("not configured", banner.Find("[data-testid=github-signin-not-configured]").TextContent);
    }

    [Fact]
    public void banner_is_hidden_on_the_github_page_which_offers_the_sign_in_itself()
    {
        Use(_github.CreateSession());
        Services.GetRequiredService<NavigationManager>().NavigateTo("/github");

        Assert.Empty(Render<GitHubSignInBanner>().FindAll("[role=alert]"));
    }

    [Fact]
    public void page_offers_the_sign_in_when_signed_out_and_shows_a_failed_sign_in()
    {
        Use(_github.CreateSession());
        Services.GetRequiredService<NavigationManager>().NavigateTo("/github?error=GitHub%20did%20not%20complete%20the%20sign-in.");

        IRenderedComponent<GitHubPage> page = Render<GitHubPage>();

        Assert.NotNull(page.Find("[data-testid=github-signed-out] [data-testid=github-signin]"));
        Assert.Equal("GitHub did not complete the sign-in.", page.Find("[data-testid=github-error]").TextContent);
    }

    [Fact]
    public void page_shows_the_user_and_the_repositories_the_app_reaches_with_links_to_manage_them()
    {
        Use(_github.CreateSession(new MemoryCredentialStore(StubGitHub.SignedIn())));
        _github.Installations.Add(new { id = 5, account = new { login = "acme" }, repository_selection = "selected", html_url = "https://github.com/organizations/acme/settings/installations/5", app_slug = "webdevloop" });
        _github.InstallationRepositories[5] = ["acme/widgets"];
        _repositories.Repositories.AddRange([ApiData.Repository(1, "widgets"), ApiData.Repository(2, "gadgets")]);

        IRenderedComponent<GitHubPage> page = Render<GitHubPage>();

        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-testid=github-installation]")));
        Assert.Equal("octocat", page.Find("[data-testid=github-login]").TextContent);
        Assert.Equal("https://github.com/organizations/acme/settings/installations/5", page.Find("[data-testid=github-manage-access]").GetAttribute("href"));
        Assert.Contains("1 selected repository", page.Find("[data-testid=github-installation]").TextContent);
        Assert.Equal("https://github.com/apps/webdevloop/installations/new", page.Find("[data-testid=github-install]").GetAttribute("href"));
        Assert.Equal(
            [("acme/widgets", "true"), ("acme/gadgets", "false")],
            page.FindAll("[data-testid=github-registered-repository]").Select(row => (row.QuerySelector("td")!.TextContent, row.GetAttribute("data-reachable")!)));
    }

    [Fact]
    public void signing_out_after_confirmation_forgets_the_user()
    {
        var store = new MemoryCredentialStore(StubGitHub.SignedIn());
        Use(_github.CreateSession(store));
        IRenderedComponent<GitHubPage> page = Render<GitHubPage>();

        page.Find("[data-testid=github-signout]").Click();
        page.Find("[data-testid=github-confirm-signout]").Click();

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid=github-signed-out]")));
        Assert.Null(store.Stored);
    }

    private GitHubUserSession Use(GitHubUserSession session)
    {
        Services.AddSingleton(session);
        Services.AddSingleton<IGitHubSignInState>(session);
        Services.AddSingleton(new GitHubAppAccess(new HttpClient(_github), session, StubGitHub.Options));
        Services.AddSingleton<IRepositoryQueries>(_repositories);
        return session;
    }
}
