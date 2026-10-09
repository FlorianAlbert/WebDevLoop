using Microsoft.AspNetCore.Components;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Queries;
using WebDevLoop.Web.Components.Repositories;

namespace WebDevLoop.Web.Components.Dashboard;

/// <summary>
/// Loads its data once and reloads it whenever a relevant workflow event is published on the in-process event bus
/// or the current repository / repository list changes.
/// Reloads are coalesced: events arriving during a load trigger exactly one more load afterwards.
/// </summary>
public abstract class LiveComponentBase : ScopedComponentBase, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private IDisposable? _subscription;
    private bool _loading;
    private bool _reloadRequested;
    private bool _disposed;

    [Inject]
    private IRunEventBus EventBus { get; set; } = default!;

    [Inject]
    protected RepositoryContext RepositoryContext { get; set; } = default!;

    protected bool IsLoaded { get; private set; }

    protected string? LoadError { get; private set; }

    protected abstract Task LoadAsync(CancellationToken cancellationToken);

    protected virtual bool ReactsTo(LiveEventView liveEvent) => false;

    protected override async Task OnInitializedAsync()
    {
        _subscription = EventBus.Subscribe(OnEventAsync);
        RepositoryContext.Changed += OnRepositoryContextChanged;
        await ReloadAsync();
    }

    protected async Task ReloadAsync()
    {
        if (_loading)
        {
            _reloadRequested = true;
            return;
        }

        _loading = true;
        try
        {
            do
            {
                _reloadRequested = false;
                await TryLoadAsync();
            }
            while (_reloadRequested && !_disposed);
        }
        finally
        {
            _loading = false;
        }

        IsLoaded = true;
        if (!_disposed)
        {
            StateHasChanged();
        }
    }

    private async Task TryLoadAsync()
    {
        try
        {
            await LoadAsync(_lifetime.Token);
            LoadError = null;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LoadError = exception.Message;
        }
    }

    // Not awaited: the bus awaits its handlers, and a slow query must not delay other subscribers or the publisher.
    private Task OnEventAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        if (!_disposed && ReactsTo(envelope.ToView()))
        {
            _ = InvokeAsync(ReloadAsync);
        }

        return Task.CompletedTask;
    }

    private void OnRepositoryContextChanged()
    {
        if (!_disposed)
        {
            _ = InvokeAsync(ReloadAsync);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        RepositoryContext.Changed -= OnRepositoryContextChanged;
        _subscription?.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}
