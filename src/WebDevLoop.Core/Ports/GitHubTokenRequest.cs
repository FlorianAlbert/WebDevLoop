using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

/// <param name="AllowUserTokenFallback">Fall back to the configured PAT/user token only when the App cannot act and fallback is enabled.</param>
public sealed record GitHubTokenRequest(GitHubRepoRef Repo, GitHubPermissionSet Permissions, bool AllowUserTokenFallback = false);
