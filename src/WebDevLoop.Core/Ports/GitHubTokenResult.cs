using System.Diagnostics.CodeAnalysis;

namespace WebDevLoop.Core.Ports;

public sealed record GitHubTokenResult
{
    private GitHubTokenResult(GitHubAccessToken? token, string? unavailableReason)
    {
        Token = token;
        UnavailableReason = unavailableReason;
    }

    public GitHubAccessToken? Token { get; }

    public string? UnavailableReason { get; }

    [MemberNotNullWhen(true, nameof(Token))]
    [MemberNotNullWhen(false, nameof(UnavailableReason))]
    public bool IsAvailable => Token is not null;

    public static GitHubTokenResult Available(GitHubAccessToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return new(token, null);
    }

    public static GitHubTokenResult Unavailable(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(null, reason);
    }
}
