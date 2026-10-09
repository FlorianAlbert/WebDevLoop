using Microsoft.EntityFrameworkCore;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Persistence.Repositories;

public sealed class EfSpecRunRepository(WebDevLoopDbContext context) : ISpecRunRepository
{
    public async Task<SpecRun?> GetAsync(RunId id, CancellationToken cancellationToken) =>
        await context.SpecRuns.FirstOrDefaultAsync(run => run.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SpecRun>> ListByRepositoryAsync(int repositoryId, CancellationToken cancellationToken) =>
        await context.SpecRuns
            .Where(run => run.RepositoryId == repositoryId)
            .OrderBy(run => run.QueuePosition)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SpecRun>> ListNonTerminalAsync(CancellationToken cancellationToken)
    {
        SpecRunStatus[] terminal = StatusSets.Terminal<SpecRunStatus>(SpecRunStatusRules.IsTerminal);

        return await context.SpecRuns
            .Where(run => !terminal.Contains(run.Status))
            .OrderBy(run => run.RepositoryId)
            .ThenBy(run => run.QueuePosition)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SpecDependency>> ListDependenciesAsync(RunId blockedSpecRunId, CancellationToken cancellationToken) =>
        await context.SpecDependencies
            .Where(dependency => dependency.BlockedSpecRunId == blockedSpecRunId)
            .OrderBy(dependency => dependency.Id)
            .ToListAsync(cancellationToken);

    public async Task<SpecRunStatus?> GetCommittedStatusAsync(RunId id, CancellationToken cancellationToken) =>
        await context.SpecRuns.AsNoTracking()
            .Where(run => run.Id == id)
            .Select(run => (SpecRunStatus?)run.Status)
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(SpecRun specRun) => context.SpecRuns.Add(specRun);

    public void AddDependency(SpecDependency dependency) => context.SpecDependencies.Add(dependency);
}
