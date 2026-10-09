using WebDevLoop.Infrastructure.GitHub.Auth;

namespace WebDevLoop.Infrastructure.GitHub.Pulls;

public sealed class GitHubPullsOptions
{
    public const string DefaultGraphQlUrl = "https://api.github.com/graphql";

    public string ApiBaseUrl { get; init; } = GitHubAuthOptions.DefaultApiBaseUrl;

    public string GraphQlUrl { get; init; } = DefaultGraphQlUrl;

    /// <summary>Let the token provider hand out the configured PAT when the App cannot act (only effective if the provider allows it).</summary>
    public bool AllowUserTokenFallback { get; init; }
}
