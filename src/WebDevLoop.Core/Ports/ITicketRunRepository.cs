using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

public interface ITicketRunRepository
{
    Task<TicketRun?> GetAsync(TicketRunId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<TicketRun>> ListBySpecRunAsync(RunId specRunId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TicketDependency>> ListDependenciesAsync(RunId specRunId, CancellationToken cancellationToken);

    void Add(TicketRun ticketRun);

    void AddDependency(TicketDependency dependency);
}
