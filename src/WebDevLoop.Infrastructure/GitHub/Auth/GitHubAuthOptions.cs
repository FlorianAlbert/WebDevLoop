namespace WebDevLoop.Infrastructure.GitHub.Auth;

public sealed class GitHubAuthOptions
{
    public const string DefaultApiBaseUrl = "https://api.github.com/";

    public string ApiBaseUrl { get; init; } = DefaultApiBaseUrl;

    /// <summary>GitHub App client id (preferred) or app id; used as the JWT issuer.</summary>
    public string? AppClientId { get; init; }

    public string? AppPrivateKeyPem { get; init; }

    /// <summary>Fine-grained PAT or user token, only handed out when <see cref="PatFallbackEnabled"/> and the caller allows it.</summary>
    public string? UserToken { get; init; }

    public bool PatFallbackEnabled { get; init; }

    /// <summary>Cached installation tokens are refreshed once they expire within this window.</summary>
    public TimeSpan ExpirySkew { get; init; } = TimeSpan.FromMinutes(5);

    public bool IsAppConfigured => !string.IsNullOrWhiteSpace(AppClientId) && !string.IsNullOrWhiteSpace(AppPrivateKeyPem);
}
