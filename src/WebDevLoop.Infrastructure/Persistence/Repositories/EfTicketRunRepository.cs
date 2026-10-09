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

    public async Task<ImplementerSlotUsage> CountOccupiedImplementerSlotsAsync(int repositoryId, CancellationToken cancellationToken)
    {
        TicketRunStatus[] occupying = StatusSets.Active<TicketRunStatus>(TicketRunStatusRules.OccupiesImplementerSlot);
        SpecRunStatus[] terminal = StatusSets.Terminal<SpecRunStatus>(SpecRunStatusRules.IsTerminal);
        List<int> repositoryIds = await context.TicketRuns.AsNoTracking()
            .Where(ticket => occupying.Contains(ticket.Status))
            .Join(
                context.SpecRuns.AsNoTracking().Where(run => !terminal.Contains(run.Status)),
                ticket => ticket.SpecRunId,
                run => run.Id,
                (_, run) => run.RepositoryId)
            .ToListAsync(cancellationToken);
        return new ImplementerSlotUsage(repositoryIds.Count, repositoryIds.Count(id => id == repositoryId));
    }

    public void Add(TicketRun ticketRun) => context.TicketRuns.Add(ticketRun);

    public void AddDependency(TicketDependency dependency) => context.TicketDependencies.Add(dependency);
}
