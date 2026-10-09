using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.GitHub.Auth;

/// <summary>
/// Git HTTPS credentials from the signed-in user's token. Whether the token may clone, fetch or push a repository is
/// decided by GitHub: the App must be installed on it with Contents access, and the user needs that access too.
/// </summary>
public sealed class GitCredentialSource(ITokenProvider tokens) : IGitCredentialSource
{
    // GitHub accepts any non-empty username for token authentication; this is the documented convention.
    private const string TokenUsername = "x-access-token";

    public async Task<GitHttpsCredential> GetCredentialAsync(GitHubRepoRef repo, GitRemoteOperation operation, CancellationToken cancellationToken)
    {
        GitHubTokenResult result = await tokens.GetTokenAsync(cancellationToken);
        return result.IsAvailable
            ? new GitHttpsCredential(TokenUsername, result.Token.Value)
            : throw new GitCredentialUnavailableException($"Cannot {operation.ToString().ToLowerInvariant()} {repo}: {result.UnavailableReason}");
    }

    public Func<GitHttpsCredential> CreateCallback(GitHubRepoRef repo, GitRemoteOperation operation) =>
        () => GetCredentialAsync(repo, operation, CancellationToken.None).GetAwaiter().GetResult();
}
