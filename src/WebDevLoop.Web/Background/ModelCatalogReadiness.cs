using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Prerequisites;

namespace WebDevLoop.Web.Background;

/// <summary>Refreshes the Model Catalog and validates current settings as one serialized operation.</summary>
public sealed partial class ModelCatalogReadiness(
    IModelCatalog catalog,
    DiagnosticReadiness readiness,
    ILogger<ModelCatalogReadiness> logger)
{
    private readonly SemaphoreSlim _refresh = new(1, 1);

    /// <summary>A failed fetch retains the last-known catalog; validation still updates readiness.</summary>
    public async Task<ModelCatalogRefreshResult> RefreshAsync(CancellationToken cancellationToken)
    {
        await _refresh.WaitAsync(cancellationToken);
        try
        {
            ModelCatalogRefreshResult result = await catalog.RefreshAsync(cancellationToken);
            if (result.Error is { } error)
            {
                LogCatalogUnavailable(logger, error);
            }

            await readiness.RefreshAsync(cancellationToken);
            return result;
        }
        finally
        {
            _refresh.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Model Catalog could not be fetched: {Error}. The last-known catalog, if any, is retained.")]
    private static partial void LogCatalogUnavailable(ILogger logger, string error);
}
