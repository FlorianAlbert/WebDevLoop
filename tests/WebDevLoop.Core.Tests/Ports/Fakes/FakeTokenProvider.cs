using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Ports.Fakes;

public sealed class FakeTokenProvider : ITokenProvider
{
    private int _generation;

    public List<GitHubTokenRequest> Requests { get; } = [];

    public bool AppCanAct { get; set; } = true;

    public Task<GitHubTokenResult> GetTokenAsync(GitHubTokenRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(AppCanAct
            ? GitHubTokenResult.Available(new GitHubAccessToken($"ghs_fake{++_generation}", GitHubTokenKind.AppInstallation, "installation-1", _generation, null))
            : GitHubTokenResult.Unavailable("GitHub App cannot act and PAT fallback is disabled."));
    }
}
