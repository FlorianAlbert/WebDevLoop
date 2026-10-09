namespace WebDevLoop.Core.Ports;

/// <summary>A GitHub credential. <see cref="ToString"/> never reveals the secret.</summary>
/// <param name="IdentityId">Installation id or user login; together with <paramref name="Generation"/> it keys Copilot runtimes.</param>
/// <param name="Generation">Increments whenever the provider mints a replacement token for the same identity.</param>
public sealed class GitHubAccessToken(string value, GitHubTokenKind kind, string identityId, int generation, DateTimeOffset? expiresAt)
{
    public string Value { get; } = value;

    public GitHubTokenKind Kind { get; } = kind;

    public string IdentityId { get; } = identityId;

    public int Generation { get; } = generation;

    public DateTimeOffset? ExpiresAt { get; } = expiresAt;

    public override string ToString() => $"{Kind} token for {IdentityId} (generation {Generation}, ***)";
}
