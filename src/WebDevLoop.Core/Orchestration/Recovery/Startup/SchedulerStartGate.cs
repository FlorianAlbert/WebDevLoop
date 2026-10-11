namespace WebDevLoop.Core.Orchestration.Recovery.Startup;

/// <summary>
/// Allows work after startup recovery and while prerequisites are healthy (register as a singleton). Closing the gate
/// pauses new work without cancelling in-flight work; restoring readiness resumes it without repeating startup recovery.
/// </summary>
public sealed class SchedulerStartGate
{
    private readonly object _lock = new();
    private TaskCompletionSource _opened = NewSignal();
    private bool _startupCompleted;
    private bool _prerequisitesReady = true;

    public bool IsOpen
    {
        get
        {
            lock (_lock)
            {
                return _opened.Task.IsCompleted;
            }
        }
    }

    public bool StartupCompleted
    {
        get
        {
            lock (_lock)
            {
                return _startupCompleted;
            }
        }
    }

    /// <summary>Marks startup recovery complete; opens only if prerequisites are still healthy. Idempotent.</summary>
    public void Open()
    {
        lock (_lock)
        {
            _startupCompleted = true;
            UpdateSignal();
        }
    }

    /// <summary>Closes or resumes the gate immediately, but never opens it before startup recovery completes.</summary>
    public void SetPrerequisitesReady(bool ready)
    {
        lock (_lock)
        {
            _prerequisitesReady = ready;
            UpdateSignal();
        }
    }

    public async Task WaitUntilOpenAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task signal;
            lock (_lock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_opened.Task.IsCompleted)
                {
                    return;
                }

                signal = _opened.Task;
            }

            await signal.WaitAsync(cancellationToken);
        }
    }

    /// <summary>Admits a launch atomically with readiness changes; closing afterwards does not cancel its result.</summary>
    public async Task<T> RunWhenOpenAsync<T>(Func<Task<T>> launch, CancellationToken cancellationToken)
    {
        Task<T> result;
        while (true)
        {
            await WaitUntilOpenAsync(cancellationToken);
            lock (_lock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_opened.Task.IsCompleted)
                {
                    continue;
                }

                result = launch();
                break;
            }
        }

        return await result;
    }

    private void UpdateSignal()
    {
        if (_startupCompleted && _prerequisitesReady)
        {
            _opened.TrySetResult();
        }
        else if (_opened.Task.IsCompleted)
        {
            _opened = NewSignal();
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
