namespace WebDevLoop.Web.Tests.Workflow;

/// <summary>
/// One-shot rendezvous for the first <c>participants</c> callers: each waits until all have arrived, so they provably overlap.
/// Callers beyond that pass straight through, and a participant that never arrives fails the waiting callers with a <see cref="TimeoutException"/>.
/// </summary>
internal sealed class OverlapGate(int participants)
{
    private static readonly TimeSpan ArrivalTimeout = TimeSpan.FromSeconds(10);

    private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrived;

    public bool Opened => _allArrived.Task.IsCompletedSuccessfully;

    public async Task ArriveAsync(CancellationToken cancellationToken)
    {
        int position = Interlocked.Increment(ref _arrived);
        if (position == participants)
        {
            _allArrived.TrySetResult();
        }

        if (position <= participants)
        {
            await _allArrived.Task.WaitAsync(ArrivalTimeout, cancellationToken);
        }
    }
}
