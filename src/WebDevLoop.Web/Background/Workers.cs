using WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;
using WebDevLoop.Core.Orchestration.Recovery.Startup;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Events;
using WebDevLoop.Web.DependencyInjection;

namespace WebDevLoop.Web.Background;

/// <summary>Publishes pending outbox messages to the in-process bus; keeps going while messages are pending, else polls.</summary>
public sealed class OutboxDispatchWorker(IServiceScopeFactory scopes, SchedulerStartGate schedulers, WorkflowWorkerOptions options, ILogger<OutboxDispatchWorker> logger)
    : GatedPeriodicWorker(scopes, schedulers, logger)
{
    protected override TimeSpan Interval => options.OutboxPollInterval;

    protected override async Task<bool> RunPassAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        await services.GetRequiredService<OutboxDispatcher>().DispatchPendingAsync(cancellationToken) > 0;
}

/// <summary>Polls ready PR stacks for the human merge and resumes interrupted completions (<see cref="MergeTrackingService.TrackAllAsync"/>).</summary>
public sealed class MergeTrackingWorker(IServiceScopeFactory scopes, SchedulerStartGate schedulers, WorkflowWorkerOptions options, ILogger<MergeTrackingWorker> logger)
    : GatedPeriodicWorker(scopes, schedulers, logger)
{
    protected override TimeSpan Interval => options.MergeTrackingInterval;

    protected override async Task<bool> RunPassAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await services.GetRequiredService<MergeTrackingService>().TrackAllAsync(cancellationToken);
        return false;
    }
}

/// <summary>
/// Stops Copilot runtimes no session used for their idle timeout and replaces runtimes whose App token is about to expire,
/// more often than the full recovery cycle (which does the same) so token refresh never waits for it.
/// </summary>
public sealed class CopilotRuntimeMaintenanceWorker(IServiceScopeFactory scopes, SchedulerStartGate schedulers, WorkflowWorkerOptions options, ILogger<CopilotRuntimeMaintenanceWorker> logger)
    : GatedPeriodicWorker(scopes, schedulers, logger)
{
    protected override TimeSpan Interval => options.RuntimeMaintenanceInterval;

    protected override async Task<bool> RunPassAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        ICopilotRuntimePool runtimes = services.GetRequiredService<ICopilotRuntimePool>();
        await runtimes.EvictIdleAsync(cancellationToken);
        await runtimes.RefreshExpiringAsync(cancellationToken);
        return false;
    }
}

/// <summary>Deletes dispatched outbox messages older than <see cref="WorkflowWorkerOptions.OutboxRetention"/>.</summary>
public sealed class OutboxRetentionWorker(IServiceScopeFactory scopes, SchedulerStartGate schedulers, WorkflowWorkerOptions options, ILogger<OutboxRetentionWorker> logger)
    : GatedPeriodicWorker(scopes, schedulers, logger)
{
    protected override TimeSpan Interval => options.OutboxPurgeInterval;

    protected override async Task<bool> RunPassAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await services.GetRequiredService<OutboxRetention>().PurgeDispatchedAsync(options.OutboxRetention, cancellationToken);
        return false;
    }
}
