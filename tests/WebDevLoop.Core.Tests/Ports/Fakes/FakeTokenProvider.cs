using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Ports.Fakes;

public sealed class FakeTokenProvider : ITokenProvider
{
    private int _generation;

    public int Requests { get; private set; }

    public bool SignedIn { get; set; } = true;

    public Task<GitHubTokenResult> GetTokenAsync(CancellationToken cancellationToken)
    {
        Requests++;
        return Task.FromResult(SignedIn
            ? GitHubTokenResult.Available(new GitHubAccessToken($"ghu_fake{++_generation}", "octocat", _generation, null))
            : GitHubTokenResult.Unavailable("Nobody is signed in to GitHub."));
    }
}
