using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Queries;

/// <summary>Read-only projections of run state for the API and UI. Lookups return <c>null</c> when the run does not exist; lists are empty.</summary>
public interface IRunQueries
{
    /// <summary>Ordered by queue position.</summary>
    Task<IReadOnlyList<SpecRunView>> ListSpecRunsAsync(int repositoryId, CancellationToken cancellationToken);

    Task<SpecRunView?> GetSpecRunAsync(RunId specRunId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TicketRunView>> ListTicketRunsAsync(RunId specRunId, CancellationToken cancellationToken);

    Task<TicketRunView?> GetTicketRunAsync(TicketRunId ticketRunId, CancellationToken cancellationToken);

    Task<IReadOnlyList<StepRunView>> ListStepsAsync(TicketRunId ticketRunId, CancellationToken cancellationToken);

    Task<StepRunView?> GetStepAsync(StepRunId stepRunId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RunEventView>> ListEventsAsync(RunId specRunId, CancellationToken cancellationToken);

    /// <summary>The spec issues blocking a spec run, in the order the dependencies were recorded.</summary>
    Task<IReadOnlyList<SpecDependencyView>> ListSpecDependenciesAsync(RunId specRunId, CancellationToken cancellationToken);

    /// <summary>Blocking specs of every spec run of the repository that has any, keyed by the blocked spec run id.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<SpecDependencyView>>> ListSpecDependenciesForRepositoryAsync(int repositoryId, CancellationToken cancellationToken);

    /// <summary><c>null</c> when no integration saga was started for the ticket.</summary>
    Task<IntegrationSagaView?> GetLatestSagaAsync(TicketRunId ticketRunId, CancellationToken cancellationToken);

    /// <summary>The latest saga of every ticket of the spec run that has one, keyed by ticket run id.</summary>
    Task<IReadOnlyDictionary<string, IntegrationSagaView>> ListLatestSagasAsync(RunId specRunId, CancellationToken cancellationToken);

    /// <summary>Bottom to top.</summary>
    Task<IReadOnlyList<StackLayerView>> ListStackAsync(RunId specRunId, CancellationToken cancellationToken);
}
