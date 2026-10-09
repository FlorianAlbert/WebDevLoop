using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Auth;
using WebDevLoop.Infrastructure.GitHub.Pulls;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Auth;

public sealed class GitHubAppAccessTests
{
    private readonly FakeGitHubOAuthHandler _github = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task installations_list_the_repositories_the_user_reaches_with_their_configure_page()
    {
        _github.Api("/user/installations?per_page=100", new
        {
            total_count = 2,
            installations = new object[]
            {
                new { id = 1, account = new { login = "octocat" }, repository_selection = "selected", html_url = "https://github.com/settings/installations/1", app_slug = "webdevloop" },
                new { id = 2, account = new { login = "acme" }, repository_selection = "all", html_url = "https://github.com/organizations/acme/settings/installations/2", app_slug = "webdevloop" },
            },
        });
        _github.Api(
            "/user/installations/1/repositories?per_page=100",
            new { repositories = new[] { new { full_name = "octocat/zeta" } } },
            nextLink: "https://api.github.com/user/installations/1/repositories?per_page=100&page=2");
        _github.Api("/user/installations/1/repositories?per_page=100&page=2", new { repositories = new[] { new { full_name = "octocat/alpha" } } });
        _github.Api("/user/installations/2/repositories?per_page=100", new { repositories = new[] { new { full_name = "acme/widgets" } } });
        GitHubAppAccess access = CreateAccess(new GitHubAuthOptions());

        IReadOnlyList<GitHubAppInstallation> installations = await access.ListInstallationsAsync(Ct);

        Assert.Equal(
            [
                new GitHubAppInstallation(1, "octocat", false, "https://github.com/settings/installations/1", "webdevloop", ["octocat/alpha", "octocat/zeta"]),
                new GitHubAppInstallation(2, "acme", true, "https://github.com/organizations/acme/settings/installations/2", "webdevloop", ["acme/widgets"]),
            ],
            installations,
            (expected, actual) => expected with { Repositories = [] } == actual with { Repositories = [] } && expected.Repositories.SequenceEqual(actual.Repositories));
        Assert.All(_github.Requests, request => Assert.Equal("Bearer ghu_user", request.Authorization));
        Assert.Equal(new Uri("https://github.com/apps/webdevloop/installations/new"), access.InstallUri(installations));
    }

    [Fact]
    public void install_link_uses_the_configured_slug_and_is_absent_without_one()
    {
        Assert.Equal(
            new Uri("https://github.com/apps/my-app/installations/new"),
            CreateAccess(new GitHubAuthOptions { AppSlug = "my-app" }).InstallUri([]));
        Assert.Null(CreateAccess(new GitHubAuthOptions()).InstallUri([]));
    }

    [Fact]
    public async Task listing_without_a_sign_in_fails_without_calling_github()
    {
        var access = new GitHubAppAccess(
            new HttpClient(_github),
            new RecordingTokenProvider(GitHubTokenResult.Unavailable("Nobody is signed in to GitHub.")),
            new GitHubAuthOptions());

        GitHubTokenUnavailableException exception = await Assert.ThrowsAsync<GitHubTokenUnavailableException>(() => access.ListInstallationsAsync(Ct));

        Assert.Equal("Nobody is signed in to GitHub.", exception.Reason);
        Assert.Empty(_github.Requests);
    }

    private GitHubAppAccess CreateAccess(GitHubAuthOptions options) => new(
        new HttpClient(_github),
        new RecordingTokenProvider(GitHubTokenResult.Available(new GitHubAccessToken("ghu_user", "octocat", 1, null))),
        options);
}
