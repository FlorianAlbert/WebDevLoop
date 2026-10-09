namespace WebDevLoop.Core.Orchestration.Recovery.Startup;

/// <summary>
/// Opened once startup recovery finished with healthy prerequisites (register as a singleton). Hosted schedulers (outbox
/// worker, repository queues, frontier dispatch, merge polling, cleanup) await <see cref="WaitUntilOpenAsync"/> before
/// their first pass, so they never run on unrecovered state or while the app is diagnostic-only.
/// </summary>
public sealed class SchedulerStartGate
{
    private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsOpen => _opened.Task.IsCompleted;

    /// <summary>Idempotent.</summary>
    public void Open() => _opened.TrySetResult();

    public Task WaitUntilOpenAsync(CancellationToken cancellationToken) => _opened.Task.WaitAsync(cancellationToken);
}
