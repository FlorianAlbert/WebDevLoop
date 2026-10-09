namespace WebDevLoop.Infrastructure.GitHub.Auth;

/// <summary>The GitHub App users sign in with (its client id and a client secret) and the GitHub endpoints.</summary>
public sealed class GitHubAuthOptions
{
    public const string DefaultApiBaseUrl = "https://api.github.com/";
    public const string DefaultWebBaseUrl = "https://github.com/";

    public string ApiBaseUrl { get; init; } = DefaultApiBaseUrl;

    /// <summary>Where users authorize the App (<c>login/oauth/authorize</c>) and codes are exchanged for tokens.</summary>
    public string WebBaseUrl { get; init; } = DefaultWebBaseUrl;

    public string? AppClientId { get; init; }

    public string? AppClientSecret { get; init; }

    /// <summary>The App's URL name (<c>github.com/apps/&lt;slug&gt;</c>); optional, enables the "Install the App" link.</summary>
    public string? AppSlug { get; init; }

    /// <summary>The user's access token is refreshed once it expires within this window.</summary>
    public TimeSpan ExpirySkew { get; init; } = TimeSpan.FromMinutes(5);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(AppClientId) && !string.IsNullOrWhiteSpace(AppClientSecret);
}
