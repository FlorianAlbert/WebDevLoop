namespace WebDevLoop.Infrastructure.GitHub.Auth;

/// <summary>
/// The signed-in user's GitHub App user access token, its refresh token and who it belongs to. Expiry times are null
/// when the App issues non-expiring user tokens. <see cref="ToString"/> never reveals the tokens.
/// </summary>
public sealed record GitHubUserCredentials
{
    public required string AccessToken { get; init; }

    public DateTimeOffset? AccessTokenExpiresAt { get; init; }

    public string? RefreshToken { get; init; }

    public DateTimeOffset? RefreshTokenExpiresAt { get; init; }

    public required string Login { get; init; }

    public long UserId { get; init; }

    public string? AvatarUrl { get; init; }

    public override string ToString() => $"GitHub user credentials for {Login} (***)";
}
