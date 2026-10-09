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

    /// <summary>
    /// The committed status of a run, read without tracking, so a long-lived unit of work sees changes other scopes
    /// committed (e.g. an abort); null when the run does not exist.
    /// </summary>
    Task<SpecRunStatus?> GetCommittedStatusAsync(RunId id, CancellationToken cancellationToken);

    void Add(SpecRun specRun);

    void AddDependency(SpecDependency dependency);
}
