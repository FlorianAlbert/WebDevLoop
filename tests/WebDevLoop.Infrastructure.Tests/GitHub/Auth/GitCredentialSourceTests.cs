using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Auth;
using WebDevLoop.Infrastructure.Tests.Copilot.Fakes;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Auth;

public sealed class GitCredentialSourceTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly GitHubRepoRef Repo = new("acme", "widgets");

    private static GitHubTokenResult UserToken(string value = "ghu_one", int generation = 1) =>
        GitHubTokenResult.Available(new GitHubAccessToken(value, "octocat", generation, Start.AddHours(8)));

    [Fact]
    public async Task credential_uses_x_access_token_as_username_and_the_token_as_password()
    {
        var source = new GitCredentialSource(new RecordingTokenProvider(UserToken("ghu_one")));

        GitHttpsCredential credential = await source.GetCredentialAsync(Repo, GitRemoteOperation.Clone, CancellationToken.None);

        Assert.Equal("x-access-token", credential.Username);
        Assert.Equal("ghu_one", credential.Password);
        Assert.DoesNotContain("ghu_one", credential.ToString());
    }

    [Fact]
    public async Task unavailable_token_raises_a_credential_exception_with_the_operation_and_reason()
    {
        var source = new GitCredentialSource(new RecordingTokenProvider(GitHubTokenResult.Unavailable("Nobody is signed in.")));

        var exception = await Assert.ThrowsAsync<GitCredentialUnavailableException>(
            () => source.GetCredentialAsync(Repo, GitRemoteOperation.Fetch, CancellationToken.None));

        Assert.Equal("Cannot fetch acme/widgets: Nobody is signed in.", exception.Message);
    }

    [Fact]
    public void per_operation_callback_obtains_the_latest_token_not_the_clone_time_token()
    {
        var clock = new TestClock(Start);
        Func<GitHttpsCredential> callback = new GitCredentialSource(new CopilotTokenProviderFake(clock)).CreateCallback(Repo, GitRemoteOperation.Clone);

        GitHttpsCredential atCloneStart = callback();
        clock.Advance(CopilotTokenProviderFake.Lifetime - TimeSpan.FromMinutes(4));
        GitHttpsCredential midClone = callback();

        Assert.Equal("ghu_gen1", atCloneStart.Password);
        Assert.Equal("ghu_gen2", midClone.Password);
    }

    [Fact]
    public void callback_raises_a_credential_exception_when_no_token_is_available()
    {
        var source = new GitCredentialSource(new RecordingTokenProvider(GitHubTokenResult.Unavailable("No token.")));

        Assert.Throws<GitCredentialUnavailableException>(source.CreateCallback(Repo, GitRemoteOperation.Push));
    }

    [Fact]
    public void user_token_maps_to_a_runtime_pool_identity_of_the_login_with_its_generation_exposed()
    {
        GitHubAccessToken token = UserToken(generation: 3).Token!;

        Assert.Equal(new CopilotAuthIdentity("octocat"), token.ToCopilotIdentity());
        Assert.Equal(3, token.Generation);
        Assert.Equal(Start.AddHours(8), token.ExpiresAt);
    }
}
