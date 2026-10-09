using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

public interface IIntegrationSagaRepository
{
    /// <summary>The most recent saga for the ticket, if any.</summary>
    Task<IntegrationSaga?> FindLatestForTicketAsync(TicketRunId ticketRunId, CancellationToken cancellationToken);

    Task<IReadOnlyList<IntegrationSaga>> ListIncompleteAsync(CancellationToken cancellationToken);

    void Add(IntegrationSaga saga);
}
