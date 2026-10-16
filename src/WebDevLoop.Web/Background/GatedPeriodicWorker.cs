using WebDevLoop.Core.Orchestration.Recovery.Startup;

namespace WebDevLoop.Web.Background;

/// <summary>
/// A hosted worker that waits on the <see cref="SchedulerStartGate"/> before every pass, including immediate follow-up
/// passes. Each pass runs in a DI scope of its own; a failing pass is logged and retried on the next tick.
/// </summary>
public abstract partial class GatedPeriodicWorker(IServiceScopeFactory scopes, SchedulerStartGate schedulers, ILogger logger) : BackgroundService
{
    protected abstract TimeSpan Interval { get; }

    /// <returns>Whether another pass should follow right away (more work is pending) instead of waiting for the interval.</returns>
    protected abstract Task<bool> RunPassAsync(IServiceProvider services, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await schedulers.WaitUntilOpenAsync(stoppingToken);
            if (!await TryRunPassAsync(stoppingToken))
            {
                await Task.Delay(Interval, stoppingToken);
            }
        }
    }

    private async Task<bool> TryRunPassAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            return await RunPassAsync(scope.ServiceProvider, stoppingToken);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            // Includes timeouts (TaskCanceledException from HttpClient): only shutdown may end the worker.
            LogPassFailed(logger, exception, GetType().Name);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{Worker} pass failed; retrying on the next tick.")]
    private static partial void LogPassFailed(ILogger logger, Exception exception, string worker);
}
