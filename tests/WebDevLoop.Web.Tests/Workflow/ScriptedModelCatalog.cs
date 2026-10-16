using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Copilot;

namespace WebDevLoop.Web.Tests.Workflow;

/// <summary>A scripted Copilot listing with the production in-memory catalog, beside the scripted agent runner.</summary>
internal sealed class ScriptedModelCatalog
{
    public ScriptedModelCatalog() => Catalog = new InMemoryModelCatalog(FetchAsync);

    public IModelCatalog Catalog { get; }

    public IReadOnlyList<ModelCatalogEntry> Models { get; set; } =
    [
        new("claude-sonnet-5.5", "Claude Sonnet 5.5", ["medium", "high"]),
        new("claude-opus-5.5", "Claude Opus 5.5", ["medium", "high"]),
    ];

    public string? Failure { get; set; }

    private Task<IReadOnlyList<ModelCatalogEntry>> FetchAsync(CancellationToken cancellationToken) =>
        Failure is { } error
            ? Task.FromException<IReadOnlyList<ModelCatalogEntry>>(new InvalidOperationException(error))
            : Task.FromResult(Models);
}
