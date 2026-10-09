using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Auth;

internal sealed class RecordingTokenProvider(GitHubTokenResult result) : ITokenProvider
{
    public List<GitHubTokenRequest> Requests { get; } = [];

    public Task<GitHubTokenResult> GetTokenAsync(GitHubTokenRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(result);
    }
}
