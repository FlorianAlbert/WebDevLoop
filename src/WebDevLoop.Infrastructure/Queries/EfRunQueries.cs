using Microsoft.EntityFrameworkCore;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Queries;
using WebDevLoop.Infrastructure.Persistence;

namespace WebDevLoop.Infrastructure.Queries;

/// <summary>No-tracking reads of run state; nothing loaded here can be saved back by accident.</summary>
public sealed class EfRunQueries(WebDevLoopDbContext context) : IRunQueries
{
    public async Task<IReadOnlyList<SpecRunView>> ListSpecRunsAsync(int repositoryId, CancellationToken cancellationToken)
    {
        List<SpecRun> runs = await context.SpecRuns.AsNoTracking()
            .Where(run => run.RepositoryId == repositoryId)
            .OrderBy(run => run.QueuePosition)
            .ToListAsync(cancellationToken);
        return runs.Select(run => run.ToView()).ToList();
    }

    public async Task<SpecRunView?> GetSpecRunAsync(RunId specRunId, CancellationToken cancellationToken) =>
        (await context.SpecRuns.AsNoTracking().FirstOrDefaultAsync(run => run.Id == specRunId, cancellationToken))?.ToView();

    public async Task<IReadOnlyList<TicketRunView>> ListTicketRunsAsync(RunId specRunId, CancellationToken cancellationToken)
    {
        List<TicketRun> tickets = await context.TicketRuns.AsNoTracking()
            .Where(ticket => ticket.SpecRunId == specRunId)
            .OrderBy(ticket => ticket.CreatedAt)
            .ThenBy(ticket => ticket.Id)
            .ToListAsync(cancellationToken);
        List<TicketDependency> dependencies = await context.TicketDependencies.AsNoTracking()
            .Where(dependency => dependency.SpecRunId == specRunId)
            .ToListAsync(cancellationToken);

        ILookup<TicketRunId, TicketRunId> blockers = dependencies.ToLookup(dependency => dependency.BlockedTicketRunId, dependency => dependency.BlockingTicketRunId);
        return tickets.Select(ticket => ticket.ToView(blockers[ticket.Id])).ToList();
    }

    public async Task<TicketRunView?> GetTicketRunAsync(TicketRunId ticketRunId, CancellationToken cancellationToken)
    {
        TicketRun? ticket = await context.TicketRuns.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == ticketRunId, cancellationToken);
        if (ticket is null)
        {
            return null;
        }

        List<TicketRunId> blockers = await context.TicketDependencies.AsNoTracking()
            .Where(dependency => dependency.BlockedTicketRunId == ticketRunId)
            .Select(dependency => dependency.BlockingTicketRunId)
            .ToListAsync(cancellationToken);
        return ticket.ToView(blockers);
    }

    public async Task<IReadOnlyList<StepRunView>> ListStepsAsync(TicketRunId ticketRunId, CancellationToken cancellationToken)
    {
        List<StepRun> steps = await context.StepRuns.AsNoTracking()
            .Where(step => step.TicketRunId == ticketRunId)
            .OrderBy(step => step.Id)
            .ToListAsync(cancellationToken);
        return steps.Select(step => step.ToView()).ToList();
    }

    public async Task<StepRunView?> GetStepAsync(StepRunId stepRunId, CancellationToken cancellationToken) =>
        (await context.StepRuns.AsNoTracking().FirstOrDefaultAsync(step => step.Id == stepRunId, cancellationToken))?.ToView();

    public async Task<IReadOnlyList<RunEventView>> ListEventsAsync(RunId specRunId, CancellationToken cancellationToken)
    {
        List<RunEvent> events = await context.RunEvents.AsNoTracking()
            .Where(runEvent => runEvent.SpecRunId == specRunId)
            .OrderBy(runEvent => runEvent.Id)
            .ToListAsync(cancellationToken);
        return events.Select(runEvent => runEvent.ToView()).ToList();
    }

    public async Task<IReadOnlyList<StackLayerView>> ListStackAsync(RunId specRunId, CancellationToken cancellationToken)
    {
        List<PullStackLayer> layers = await context.PullStackLayers.AsNoTracking()
            .Where(layer => layer.SpecRunId == specRunId)
            .OrderBy(layer => layer.Position)
            .ToListAsync(cancellationToken);
        return layers.Select(layer => layer.ToView()).ToList();
    }
}
