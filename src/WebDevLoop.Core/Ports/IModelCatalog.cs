using WebDevLoop.Core.Agents;

namespace WebDevLoop.Core.Ports;

/// <summary>The in-memory Model Catalog for the same Copilot identity used by agent sessions.</summary>
public interface IModelCatalog
{
    IReadOnlyList<ModelCatalogEntry> Models { get; }

    /// <summary>Whether a fetch has ever succeeded in this process, including an empty catalog.</summary>
    bool HasFetched { get; }

    /// <summary>Fetches current Models. Failure preserves the last-known catalog and returns the error.</summary>
    Task<ModelCatalogRefreshResult> RefreshAsync(CancellationToken cancellationToken);
}

/// <summary>A refresh outcome; an error is surfaced without discarding the last-known Model Catalog.</summary>
public sealed record ModelCatalogRefreshResult(string? Error)
{
    public bool Succeeded => Error is null;
}
