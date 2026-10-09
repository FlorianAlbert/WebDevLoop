using Microsoft.EntityFrameworkCore;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Persistence.Repositories;

public sealed class EfIntegrationSagaRepository(WebDevLoopDbContext context) : IIntegrationSagaRepository
{
    public async Task<IntegrationSaga?> FindLatestForTicketAsync(TicketRunId ticketRunId, CancellationToken cancellationToken) =>
        await context.IntegrationSagas
            .Where(saga => saga.TicketRunId == ticketRunId)
            .OrderByDescending(saga => saga.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<IntegrationSaga>> ListIncompleteAsync(CancellationToken cancellationToken) =>
        await context.IntegrationSagas
            .Where(saga => saga.Checkpoint != IntegrationSagaCheckpoint.Completed)
            .OrderBy(saga => saga.Id)
            .ToListAsync(cancellationToken);

    public void Add(IntegrationSaga saga) => context.IntegrationSagas.Add(saga);
}
