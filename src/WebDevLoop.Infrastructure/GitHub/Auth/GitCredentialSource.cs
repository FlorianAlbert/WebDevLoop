using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.GitHub.Auth;

public sealed class GitCredentialSource(ITokenProvider tokens) : IGitCredentialSource
{
    // GitHub accepts any non-empty username for token authentication; this is the documented convention.
    private const string TokenUsername = "x-access-token";

    public async Task<GitHttpsCredential> GetCredentialAsync(
        GitHubRepoRef repo,
        GitRemoteOperation operation,
        bool allowUserTokenFallback,
        CancellationToken cancellationToken)
    {
        GitHubTokenResult result = await tokens.GetTokenAsync(
            new GitHubTokenRequest(repo, PermissionsFor(operation), allowUserTokenFallback),
            cancellationToken);

        return result.IsAvailable
            ? new GitHttpsCredential(TokenUsername, result.Token.Value)
            : throw new GitCredentialUnavailableException(result.UnavailableReason);
    }

    public Func<GitHttpsCredential> CreateCallback(GitHubRepoRef repo, GitRemoteOperation operation, bool allowUserTokenFallback = false) =>
        () => GetCredentialAsync(repo, operation, allowUserTokenFallback, CancellationToken.None).GetAwaiter().GetResult();

    private static GitHubPermissionSet PermissionsFor(GitRemoteOperation operation) => operation switch
    {
        GitRemoteOperation.Clone or GitRemoteOperation.Fetch => GitHubPermissionSet.ContentsRead,
        GitRemoteOperation.Push => GitHubPermissionSet.ContentsWrite,
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
    };
}
