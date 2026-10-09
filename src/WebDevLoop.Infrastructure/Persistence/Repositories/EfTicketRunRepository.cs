using Microsoft.EntityFrameworkCore;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Persistence.Repositories;

public sealed class EfTicketRunRepository(WebDevLoopDbContext context) : ITicketRunRepository
{
    public async Task<TicketRun?> GetAsync(TicketRunId id, CancellationToken cancellationToken) =>
        await context.TicketRuns.FirstOrDefaultAsync(ticket => ticket.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TicketRun>> ListBySpecRunAsync(RunId specRunId, CancellationToken cancellationToken) =>
        await context.TicketRuns
            .Where(ticket => ticket.SpecRunId == specRunId)
            .OrderBy(ticket => ticket.CreatedAt)
            .ThenBy(ticket => ticket.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TicketDependency>> ListDependenciesAsync(RunId specRunId, CancellationToken cancellationToken) =>
        await context.TicketDependencies
            .Where(dependency => dependency.SpecRunId == specRunId)
            .OrderBy(dependency => dependency.Id)
            .ToListAsync(cancellationToken);

    public void Add(TicketRun ticketRun) => context.TicketRuns.Add(ticketRun);

    public void AddDependency(TicketDependency dependency) => context.TicketDependencies.Add(dependency);
}
