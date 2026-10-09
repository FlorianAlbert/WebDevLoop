namespace WebDevLoop.Web.Background;

/// <summary>
/// Runs workflow work (preparation, implementers, review loops, sagas, ...) in the background, each launch in a DI scope of
/// its own, and tracks it for graceful shutdown: stopping cancels every running item and waits for it to wind down, and
/// later launches are ignored. Launches are coalesced per key: while an item with the same key runs, another launch is
/// queued to run once afterwards (the latest launch wins), so the same ticket or spec phase never runs twice concurrently
/// in this process and no launch is lost. Registered first among the hosted services so it stops last.
/// </summary>
public sealed partial class BackgroundWorkRunner(IServiceScopeFactory scopes, ILogger<BackgroundWorkRunner> logger) : IHostedService, IAsyncDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, WorkItem> _running = [];
    private bool _stopped;
    private int _disposed;

    /// <summary>How many keys have work running (or queued to rerun).</summary>
    public int RunningCount
    {
        get
        {
            lock (_gate)
            {
                return _running.Count;
            }
        }
    }

    /// <summary>Starts <paramref name="work"/> in the background and returns immediately.</summary>
    public void Run(string key, Func<IServiceProvider, CancellationToken, Task> work)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(work);
        lock (_gate)
        {
            if (_stopped)
            {
                LogIgnoredAfterStop(logger, key);
                return;
            }

            if (_running.TryGetValue(key, out WorkItem? running))
            {
                running.Next = work;
                return;
            }

            var item = new WorkItem();
            _running[key] = item;
            item.Completion = Task.Run(() => RunUntilSettledAsync(key, item, work));
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Cancels every running item and waits for it until <paramref name="cancellationToken"/> (the host's shutdown timeout).</summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task[] running;
        lock (_gate)
        {
            _stopped = true;
            running = [.. _running.Values.Select(item => item.Completion)];
        }

        if (Volatile.Read(ref _disposed) == 0)
        {
            await _stopping.CancelAsync();
        }

        try
        {
            await Task.WhenAll(running).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogShutdownTimedOut(logger, running.Count(task => !task.IsCompleted));
        }
    }

    /// <summary>
    /// Cancels whatever still runs without waiting again: the host's <see cref="StopAsync"/> already waited up to the shutdown
    /// timeout, and work that ignores cancellation must not keep the process alive. Idempotent (the container may dispose
    /// the instance both as singleton and as hosted service).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _stopped = true;
        }

        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            await _stopping.CancelAsync();
            _stopping.Dispose();
        }
    }

    private async Task RunUntilSettledAsync(string key, WorkItem item, Func<IServiceProvider, CancellationToken, Task> work)
    {
        Func<IServiceProvider, CancellationToken, Task>? next = work;
        while (next is not null)
        {
            await RunOnceAsync(key, next);
            lock (_gate)
            {
                next = _stopped ? null : item.Next;
                item.Next = null;
                if (next is null)
                {
                    _running.Remove(key);
                }
            }
        }
    }

    private async Task RunOnceAsync(string key, Func<IServiceProvider, CancellationToken, Task> work)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            await work(scope.ServiceProvider, _stopping.Token);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            LogCancelledByShutdown(logger, key);
        }
        catch (Exception exception)
        {
            LogFailed(logger, exception, key);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Background work '{Key}' failed; periodic recovery resumes it.")]
    private static partial void LogFailed(ILogger logger, Exception exception, string key);

    [LoggerMessage(Level = LogLevel.Information, Message = "Background work '{Key}' was cancelled by shutdown; recovery resumes it on the next start.")]
    private static partial void LogCancelledByShutdown(ILogger logger, string key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Ignored the launch of '{Key}' because the app is shutting down.")]
    private static partial void LogIgnoredAfterStop(ILogger logger, string key);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shutdown timed out while {Count} background work item(s) were still winding down.")]
    private static partial void LogShutdownTimedOut(ILogger logger, int count);

    private sealed class WorkItem
    {
        public Task Completion { get; set; } = Task.CompletedTask;

        public Func<IServiceProvider, CancellationToken, Task>? Next { get; set; }
    }
}
