using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.GitHub.Auth;

/// <summary>Resolves Git HTTPS credentials per remote operation so a refreshed token is never missed.</summary>
public interface IGitCredentialSource
{
    /// <exception cref="GitCredentialUnavailableException">Nobody is signed in to GitHub (or the sign-in expired).</exception>
    Task<GitHttpsCredential> GetCredentialAsync(GitHubRepoRef repo, GitRemoteOperation operation, CancellationToken cancellationToken);

    /// <summary>
    /// A callback for synchronous Git libraries (e.g. LibGit2Sharp credential providers). Every invocation resolves
    /// the current token, so it stays valid across token refreshes during long clones and pushes.
    /// </summary>
    Func<GitHttpsCredential> CreateCallback(GitHubRepoRef repo, GitRemoteOperation operation);
}
