using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Auth;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Auth;

public sealed class GitCredentialSourceTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly GitHubRepoRef Repo = new("acme", "widgets");

    private static GitHubTokenResult AppToken(string value = "ghs_one", int generation = 1) =>
        GitHubTokenResult.Available(new GitHubAccessToken(value, GitHubTokenKind.AppInstallation, "42", generation, Start.AddHours(1)));

    [Theory]
    [InlineData(GitRemoteOperation.Clone, "contents:read")]
    [InlineData(GitRemoteOperation.Fetch, "contents:read")]
    [InlineData(GitRemoteOperation.Push, "contents:write")]
    public async Task credential_requests_the_least_permissions_needed_for_the_operation(GitRemoteOperation operation, string expected)
    {
        var tokens = new RecordingTokenProvider(AppToken());

        await new GitCredentialSource(tokens).GetCredentialAsync(Repo, operation, allowUserTokenFallback: false, CancellationToken.None);

        GitHubTokenRequest request = Assert.Single(tokens.Requests);
        Assert.Equal(Repo, request.Repo);
        Assert.Equal(expected, request.Permissions.ToString());
    }

    [Fact]
    public async Task credential_uses_x_access_token_as_username_and_the_token_as_password()
    {
        var source = new GitCredentialSource(new RecordingTokenProvider(AppToken("ghs_one")));

        GitHttpsCredential credential = await source.GetCredentialAsync(Repo, GitRemoteOperation.Clone, false, CancellationToken.None);

        Assert.Equal("x-access-token", credential.Username);
        Assert.Equal("ghs_one", credential.Password);
        Assert.DoesNotContain("ghs_one", credential.ToString());
    }

    [Fact]
    public async Task user_token_fallback_is_only_requested_when_the_caller_allows_it()
    {
        var tokens = new RecordingTokenProvider(AppToken());
        var source = new GitCredentialSource(tokens);

        await source.GetCredentialAsync(Repo, GitRemoteOperation.Push, false, CancellationToken.None);
        await source.GetCredentialAsync(Repo, GitRemoteOperation.Push, true, CancellationToken.None);

        Assert.Equal([false, true], tokens.Requests.Select(request => request.AllowUserTokenFallback));
    }

    [Fact]
    public async Task unavailable_token_raises_a_credential_exception_with_the_reason()
    {
        var source = new GitCredentialSource(new RecordingTokenProvider(GitHubTokenResult.Unavailable("App not installed.")));

        var exception = await Assert.ThrowsAsync<GitCredentialUnavailableException>(
            () => source.GetCredentialAsync(Repo, GitRemoteOperation.Fetch, false, CancellationToken.None));

        Assert.Equal("App not installed.", exception.Message);
    }

    [Fact]
    public async Task per_operation_callback_obtains_the_latest_token_not_the_clone_time_token()
    {
        var clock = new TestClock(Start);
        var api = new FakeGitHubApiHandler(clock);
        var provider = new GitHubTokenProvider(
            new HttpClient(api),
            clock,
            new GitHubAuthOptions { AppClientId = "Iv1.test", AppPrivateKeyPem = TestRsaKey.PrivateKeyPem });
        Func<GitHttpsCredential> callback = new GitCredentialSource(provider).CreateCallback(Repo, GitRemoteOperation.Clone);

        GitHttpsCredential atCloneStart = callback();
        clock.Advance(TimeSpan.FromMinutes(56));
        GitHttpsCredential midClone = callback();

        Assert.Equal(FakeGitHubApiHandler.TokenValue(1), atCloneStart.Password);
        Assert.Equal(FakeGitHubApiHandler.TokenValue(2), midClone.Password);
    }

    [Fact]
    public void callback_raises_a_credential_exception_when_no_token_is_available()
    {
        var source = new GitCredentialSource(new RecordingTokenProvider(GitHubTokenResult.Unavailable("No token.")));

        Assert.Throws<GitCredentialUnavailableException>(source.CreateCallback(Repo, GitRemoteOperation.Push));
    }

    [Fact]
    public void app_installation_token_maps_to_a_runtime_pool_identity_with_its_generation_exposed()
    {
        GitHubAccessToken token = AppToken(generation: 3).Token!;

        CopilotAuthIdentity identity = token.ToCopilotIdentity();

        Assert.Equal(new CopilotAuthIdentity(CopilotAuthKind.GitHubAppInstallation, "42"), identity);
        Assert.Equal(3, token.Generation);
        Assert.Equal(Start.AddHours(1), token.ExpiresAt);
    }

    [Fact]
    public void user_token_maps_to_a_user_token_identity()
    {
        var token = new GitHubAccessToken("ghp_secret", GitHubTokenKind.UserToken, "user-token", 1, null);

        Assert.Equal(new CopilotAuthIdentity(CopilotAuthKind.UserToken, "user-token"), token.ToCopilotIdentity());
    }
}
