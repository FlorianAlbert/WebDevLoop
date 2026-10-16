using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Ports.Fakes;

/// <summary>An in-memory Model Catalog with settable state and a scripted refresh outcome.</summary>
public sealed class ScriptedModelCatalog : IModelCatalog
{
    public IReadOnlyList<ModelCatalogEntry> Models { get; set; } = [];

    public bool HasFetched { get; set; }

    public ModelCatalogRefreshResult RefreshResult { get; set; } = new(null);

    public Task<ModelCatalogRefreshResult> RefreshAsync(CancellationToken cancellationToken)
    {
        if (RefreshResult.Succeeded)
        {
            HasFetched = true;
        }

        return Task.FromResult(RefreshResult);
    }
}
