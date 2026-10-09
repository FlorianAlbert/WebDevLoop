namespace WebDevLoop.Core.Ports;

/// <summary>The signed-in user's GitHub access token. <see cref="ToString"/> never reveals the secret.</summary>
/// <param name="IdentityId">The user's login; together with <paramref name="Generation"/> it keys Copilot runtimes.</param>
/// <param name="Generation">Increments whenever the provider hands out a replacement token (sign-in, refresh).</param>
/// <param name="ExpiresAt">Null when the GitHub App issues non-expiring user tokens.</param>
public sealed class GitHubAccessToken(string value, string identityId, int generation, DateTimeOffset? expiresAt)
{
    public string Value { get; } = value;

    public string IdentityId { get; } = identityId;

    public int Generation { get; } = generation;

    public DateTimeOffset? ExpiresAt { get; } = expiresAt;

    public override string ToString() => $"GitHub user token for {IdentityId} (generation {Generation}, ***)";
}
