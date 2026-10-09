namespace WebDevLoop.Core.Ports;

/// <summary>
/// Mints/caches permission-scoped GitHub tokens. Callers request a token per operation (clone, fetch, push, API call)
/// and must not keep it beyond that operation, so refreshed tokens are always used.
/// </summary>
public interface ITokenProvider
{
    Task<GitHubTokenResult> GetTokenAsync(GitHubTokenRequest request, CancellationToken cancellationToken);
}
