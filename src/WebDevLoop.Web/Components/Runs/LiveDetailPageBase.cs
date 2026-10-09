using Microsoft.AspNetCore.Components;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Web.Components.Runs;

/// <summary>
/// Base of the detail pages. Owns a DI scope (so query services get a component-lifetime DbContext instead of sharing the
/// circuit's), loads on parameter changes, and reloads when a relevant workflow event arrives on the in-process bus.
/// </summary>
public abstract class LiveDetailPageBase : OwningComponentBase
{
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly CancellationTokenSource _disposed = new();
    private RunEventSubscription? _subscription;

    [Inject] private IRunEventBus Bus { get; set; } = default!;

    protected bool IsNotFound { get; set; }

    protected CancellationToken DisposedToken => _disposed.Token;

    protected abstract Task LoadAsync(CancellationToken cancellationToken);

    protected abstract bool IsRelevant(LiveEventView view);

    protected override void OnInitialized() => _subscription = new RunEventSubscription(Bus, IsRelevant, RefreshAsync);

    protected override Task OnParametersSetAsync() => RefreshAsync();

    protected Task RefreshAsync() => InvokeAsync(async () =>
    {
        try
        {
            await _loadGate.WaitAsync(_disposed.Token);
            try
            {
                await LoadAsync(_disposed.Token);
            }
            finally
            {
                _loadGate.Release();
            }

            StateHasChanged();
        }
        catch (OperationCanceledException) when (_disposed.IsCancellationRequested)
        {
            // The page was closed while loading.
        }
    });

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _subscription?.Dispose();
            _disposed.Cancel();
        }

        base.Dispose(disposing);
    }
}
