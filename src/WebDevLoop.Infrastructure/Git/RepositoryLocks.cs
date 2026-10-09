using System.Collections.Concurrent;

namespace WebDevLoop.Infrastructure.Git;

/// <summary>
/// Serializes mutating operations per local clone inside this process. Combined with libgit2's lock files this makes
/// read-compare-write ref updates atomic for the single WebDevLoop process that owns the workspace.
/// </summary>
internal sealed class RepositoryLocks
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<T> RunAsync<T>(string repositoryPath, Func<T> operation, CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = _locks.GetOrAdd(repositoryPath, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(operation, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }
}
