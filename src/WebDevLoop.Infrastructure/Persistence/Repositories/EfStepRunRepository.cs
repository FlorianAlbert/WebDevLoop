using Microsoft.EntityFrameworkCore;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Persistence.Repositories;

public sealed class EfStepRunRepository(WebDevLoopDbContext context) : IStepRunRepository
{
    public async Task<StepRun?> GetAsync(StepRunId id, CancellationToken cancellationToken) =>
        await context.StepRuns.FirstOrDefaultAsync(step => step.Id == id, cancellationToken);

    public async Task<IReadOnlyList<StepRun>> ListBySpecRunAsync(RunId specRunId, CancellationToken cancellationToken) =>
        await context.StepRuns
            .Where(step => step.SpecRunId == specRunId)
            .OrderBy(step => step.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StepRun>> ListByTicketRunAsync(TicketRunId ticketRunId, CancellationToken cancellationToken) =>
        await context.StepRuns
            .Where(step => step.TicketRunId == ticketRunId)
            .OrderBy(step => step.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StepRun>> ListActiveAsync(CancellationToken cancellationToken)
    {
        StepStatus[] active = StatusSets.Active<StepStatus>(StepStatusRules.IsActive);

        return await context.StepRuns
            .Where(step => active.Contains(step.Status))
            .OrderBy(step => step.Id)
            .ToListAsync(cancellationToken);
    }

    public void Add(StepRun stepRun) => context.StepRuns.Add(stepRun);
}
