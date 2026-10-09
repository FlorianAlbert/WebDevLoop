using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Tests.GitHub.Auth;

namespace WebDevLoop.Infrastructure.Tests.Copilot.Fakes;

/// <summary>
/// Hands out the signed-in user's token like <c>GitHubUserSession</c>: an expiring token is reused until it expires within
/// the skew, then refreshed with a new generation.
/// </summary>
internal sealed class CopilotTokenProviderFake(TestClock clock) : ITokenProvider
{
    public const string Login = "octocat";
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);
    private static readonly TimeSpan Skew = TimeSpan.FromMinutes(5);

    private GitHubAccessToken? _current;

    public int Requests { get; private set; }

    public string? UnavailableReason { get; set; }

    /// <summary>When false, tokens never expire (the App opted out of expiring user tokens).</summary>
    public bool TokensExpire { get; set; } = true;

    public Task<GitHubTokenResult> GetTokenAsync(CancellationToken cancellationToken)
    {
        Requests++;
        if (UnavailableReason is not null)
        {
            return Task.FromResult(GitHubTokenResult.Unavailable(UnavailableReason));
        }

        if (_current is null || _current.ExpiresAt - Skew <= clock.UtcNow)
        {
            int generation = (_current?.Generation ?? 0) + 1;
            _current = new GitHubAccessToken($"ghu_gen{generation}", Login, generation, TokensExpire ? clock.UtcNow + Lifetime : null);
        }

        return Task.FromResult(GitHubTokenResult.Available(_current));
    }

    /// <summary>Forces the next request to hand out a new generation (e.g. the session refreshed it elsewhere).</summary>
    public void Expire() => _current = null;
}
