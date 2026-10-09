using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Tests.GitHub.Auth;

namespace WebDevLoop.Infrastructure.Tests.Copilot.Fakes;

/// <summary>Mints one-hour tokens like the GitHub App provider: cached until they expire within the skew, then a new generation.</summary>
internal sealed class CopilotTokenProviderFake(TestClock clock, GitHubTokenKind kind = GitHubTokenKind.AppInstallation) : ITokenProvider
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan Skew = TimeSpan.FromMinutes(5);

    private GitHubAccessToken? _current;

    public List<GitHubTokenRequest> Requests { get; } = [];

    public string? UnavailableReason { get; set; }

    /// <summary>When false, user tokens never expire (a configured PAT).</summary>
    public bool UserTokensRotate { get; set; }

    public Task<GitHubTokenResult> GetTokenAsync(GitHubTokenRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (UnavailableReason is not null)
        {
            return Task.FromResult(GitHubTokenResult.Unavailable(UnavailableReason));
        }

        if (_current is null || _current.ExpiresAt - Skew <= clock.UtcNow)
        {
            int generation = (_current?.Generation ?? 0) + 1;
            bool expires = kind == GitHubTokenKind.AppInstallation || UserTokensRotate;
            _current = new GitHubAccessToken(
                kind == GitHubTokenKind.AppInstallation ? $"ghs_gen{generation}" : $"ghu_gen{generation}",
                kind,
                kind == GitHubTokenKind.AppInstallation ? "4242" : "user-token",
                generation,
                expires ? clock.UtcNow + Lifetime : null);
        }

        return Task.FromResult(GitHubTokenResult.Available(_current));
    }

    /// <summary>Forces the next request to mint a new generation (e.g. the provider refreshed it elsewhere).</summary>
    public void Expire() => _current = null;
}
