using System.Collections.Concurrent;
using WebDevLoop.Core.Domain;
using WebDevLoop.Infrastructure.GitHub.Auth;

namespace WebDevLoop.Infrastructure.Tests.Git;

/// <summary>Records which credential callbacks the workspace asks for; local file remotes never invoke them.</summary>
internal sealed class RecordingGitCredentialSource : IGitCredentialSource
{
    private readonly ConcurrentQueue<(GitHubRepoRef Repo, GitRemoteOperation Operation)> _requests = new();

    public IReadOnlyList<(GitHubRepoRef Repo, GitRemoteOperation Operation)> Requests => [.. _requests];

    public int Issued { get; private set; }

    public Task<GitHttpsCredential> GetCredentialAsync(
        GitHubRepoRef repo,
        GitRemoteOperation operation,
        bool allowUserTokenFallback,
        CancellationToken cancellationToken)
    {
        _requests.Enqueue((repo, operation));
        return Task.FromResult(Next());
    }

    public Func<GitHttpsCredential> CreateCallback(GitHubRepoRef repo, GitRemoteOperation operation, bool allowUserTokenFallback = false)
    {
        _requests.Enqueue((repo, operation));
        return Next;
    }

    private GitHttpsCredential Next() => new("x-access-token", $"token-{++Issued}");
}
