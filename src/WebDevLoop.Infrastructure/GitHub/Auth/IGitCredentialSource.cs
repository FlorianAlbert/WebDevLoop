using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.GitHub.Auth;

/// <summary>Resolves Git HTTPS credentials per remote operation so a refreshed token is never missed.</summary>
public interface IGitCredentialSource
{
    /// <exception cref="GitCredentialUnavailableException">No token can be provided for the operation.</exception>
    Task<GitHttpsCredential> GetCredentialAsync(
        GitHubRepoRef repo,
        GitRemoteOperation operation,
        bool allowUserTokenFallback,
        CancellationToken cancellationToken);

    /// <summary>
    /// A callback for synchronous Git libraries (e.g. LibGit2Sharp credential providers). Every invocation resolves
    /// the current token, so it stays valid across token refreshes during long clones and pushes.
    /// </summary>
    Func<GitHttpsCredential> CreateCallback(GitHubRepoRef repo, GitRemoteOperation operation, bool allowUserTokenFallback = false);
}
