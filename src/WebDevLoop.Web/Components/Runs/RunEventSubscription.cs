using WebDevLoop.Core.Events;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Web.Components.Runs;

/// <summary>
/// Subscribes to the in-process event bus and asks a component to reload its projections. Events arriving while a reload
/// runs collapse into a single follow-up reload, so bursts cannot overlap queries on the component's scope.
/// </summary>
public sealed class RunEventSubscription : IDisposable
{
    private readonly Func<LiveEventView, bool> _isRelevant;
    private readonly Func<Task> _reload;
    private readonly IDisposable _subscription;
    private readonly object _gate = new();
    private bool _reloadRequested;
    private bool _reloading;
    private volatile bool _disposed;

    public RunEventSubscription(IRunEventBus bus, Func<LiveEventView, bool> isRelevant, Func<Task> reload)
    {
        ArgumentNullException.ThrowIfNull(bus);
        _isRelevant = isRelevant;
        _reload = reload;
        _subscription = bus.Subscribe(OnEventAsync);
    }

    /// <summary>
    /// Events of other spec runs are ignored. Events without a spec run id (types the live mapper does not know yet) and
    /// every event while the run is not yet known are treated as relevant: an extra reload is cheap, a stale page is not.
    /// </summary>
    public static bool Concerns(LiveEventView view, string? specRunId) =>
        specRunId is null || view.SpecRunId is null || view.SpecRunId == specRunId;

    public void Dispose()
    {
        _disposed = true;
        _subscription.Dispose();
    }

    private async Task OnEventAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        if (_disposed || !_isRelevant(envelope.ToView()))
        {
            return;
        }

        lock (_gate)
        {
            _reloadRequested = true;
            if (_reloading)
            {
                return;
            }

            _reloading = true;
        }

        try
        {
            while (TakeReloadRequest())
            {
                await _reload();
            }
        }
        finally
        {
            lock (_gate)
            {
                _reloading = false;
            }
        }
    }

    private bool TakeReloadRequest()
    {
        lock (_gate)
        {
            bool requested = _reloadRequested && !_disposed;
            _reloadRequested = false;
            _reloading = requested;
            return requested;
        }
    }
}
