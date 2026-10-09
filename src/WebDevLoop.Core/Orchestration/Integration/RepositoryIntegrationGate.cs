using System.Collections.Concurrent;

namespace WebDevLoop.Core.Orchestration.Integration;

/// <summary>
/// Process-wide (singleton) merge lock per repository: integration sagas of one repository run one at a time, so every
/// squash builds on the previous one and stack layers form a linear chain in integration order. Correctness across
/// processes and crashes still comes from compare-and-swap ref updates on the expected prior integration tip.
/// </summary>
public sealed class RepositoryIntegrationGate : IDisposable
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _semaphores = new();

    public async Task<IDisposable> EnterAsync(int repositoryId, CancellationToken cancellationToken)
    {
        SemaphoreSlim semaphore = _semaphores.GetOrAdd(repositoryId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Lease(semaphore);
    }

    /// <returns>Null when the merge lock was not free within <paramref name="timeout"/>.</returns>
    public async Task<IDisposable?> TryEnterAsync(int repositoryId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        SemaphoreSlim semaphore = _semaphores.GetOrAdd(repositoryId, _ => new SemaphoreSlim(1, 1));
        return await semaphore.WaitAsync(timeout, cancellationToken) ? new Lease(semaphore) : null;
    }

    public void Dispose()
    {
        foreach (SemaphoreSlim semaphore in _semaphores.Values)
        {
            semaphore.Dispose();
        }
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
