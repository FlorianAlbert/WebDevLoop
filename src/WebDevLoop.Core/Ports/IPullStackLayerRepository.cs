using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

public interface IPullStackLayerRepository
{
    /// <summary>Layers ordered bottom to top by position.</summary>
    Task<IReadOnlyList<PullStackLayer>> ListBySpecRunAsync(RunId specRunId, CancellationToken cancellationToken);

    void Add(PullStackLayer layer);
}
