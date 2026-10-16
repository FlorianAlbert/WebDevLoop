using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Copilot;

/// <summary>Retains successful Copilot model listings only for the lifetime of the process.</summary>
public sealed class InMemoryModelCatalog(Func<CancellationToken, Task<IReadOnlyList<ModelCatalogEntry>>> fetch) : IModelCatalog
{
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private volatile IReadOnlyList<ModelCatalogEntry>? _models;

    public IReadOnlyList<ModelCatalogEntry> Models => _models ?? [];

    public bool HasFetched => _models is not null;

    public async Task<ModelCatalogRefreshResult> RefreshAsync(CancellationToken cancellationToken)
    {
        await _refresh.WaitAsync(cancellationToken);
        try
        {
            IReadOnlyList<ModelCatalogEntry> models = await fetch(cancellationToken);
            _models = Array.AsReadOnly(models.Select(model => model with
            {
                SupportedReasoningEfforts = Array.AsReadOnly(model.SupportedReasoningEfforts.ToArray()),
            }).ToArray());
            return new ModelCatalogRefreshResult(null);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new ModelCatalogRefreshResult(exception.Message);
        }
        finally
        {
            _refresh.Release();
        }
    }
}
