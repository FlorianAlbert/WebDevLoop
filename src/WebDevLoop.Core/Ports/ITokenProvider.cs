namespace WebDevLoop.Core.Ports;

/// <summary>
/// Hands out the signed-in user's GitHub token, refreshing it before it expires. The token is used for every GitHub
/// operation (API, clone, fetch, push) and for Copilot; which repositories it reaches is governed by the GitHub App's
/// installations. Callers request a token per operation and must not keep it beyond that operation.
/// </summary>
public interface ITokenProvider
{
    /// <returns>An unavailable result when nobody is signed in or the sign-in expired.</returns>
    Task<GitHubTokenResult> GetTokenAsync(CancellationToken cancellationToken);
}
