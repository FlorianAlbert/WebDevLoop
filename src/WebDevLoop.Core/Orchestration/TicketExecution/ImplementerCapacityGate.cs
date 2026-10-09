namespace WebDevLoop.Core.Orchestration.TicketExecution;

/// <summary>
/// Process-wide (singleton) critical section around "count occupied implementer slots, then claim": without it two
/// dispatchers could both see the last free slot. Ticket claims themselves stay safe through compare-and-swap.
/// </summary>
public sealed class ImplementerCapacityGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Lease(_semaphore);
    }

    public void Dispose() => _semaphore.Dispose();

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
