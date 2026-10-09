using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

public interface ISpecRunRepository
{
    Task<SpecRun?> GetAsync(RunId id, CancellationToken cancellationToken);

    /// <summary>All runs of a repository ordered by queue position.</summary>
    Task<IReadOnlyList<SpecRun>> ListByRepositoryAsync(int repositoryId, CancellationToken cancellationToken);

    /// <summary>Runs in any non-terminal status across all repositories (schedulers and recovery).</summary>
    Task<IReadOnlyList<SpecRun>> ListNonTerminalAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SpecDependency>> ListDependenciesAsync(RunId blockedSpecRunId, CancellationToken cancellationToken);

    void Add(SpecRun specRun);

    void AddDependency(SpecDependency dependency);
}
