using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Auth;

internal sealed class RecordingTokenProvider(GitHubTokenResult result) : ITokenProvider
{
    public int Requests { get; private set; }

    public Task<GitHubTokenResult> GetTokenAsync(CancellationToken cancellationToken)
    {
        Requests++;
        return Task.FromResult(result);
    }
}
