using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Control;

/// <summary>Retry and Abort of a spec run (see <see cref="IRunControl"/>).</summary>
public sealed class SpecRunControl(
    ISpecRunRepository specRuns,
    ITicketRunRepository ticketRuns,
    IStepRunRepository stepRuns,
    ITestLeaseRepository leases,
    SpecQueueScheduler scheduler,
    ActiveWorkStopper stopper,
    RunControlJournal journal,
    IUnitOfWork unitOfWork)
{
    public async Task<ControlResult> RetryAsync(RunId specRunId, CancellationToken cancellationToken)
    {
        if (await specRuns.GetAsync(specRunId, cancellationToken) is not { } spec)
        {
            return NotFound(specRunId);
        }

        if (spec.Status != SpecRunStatus.NeedsAttention)
        {
            return ControlResult.NotAllowed($"Spec run '{specRunId}' is {spec.Status}; only a spec run that needs attention can be retried.");
        }

        bool hasOpenTickets = (await ticketRuns.ListBySpecRunAsync(specRunId, cancellationToken)).Any(ticket => !ticket.IsTerminal);
        if (SpecRetryPlanner.Target(spec, hasOpenTickets) is not { } target)
        {
            return ControlResult.NotAllowed($"Spec run '{specRunId}' failed before it started; abort it and queue the spec again.");
        }

        // The slot it held was released when it needed attention and may belong to another spec by now.
        if (target.IsActive())
        {
            if (await scheduler.FindFreeSlotAsync(spec.RepositoryId, cancellationToken) is not { } slot)
            {
                return new ControlResult(
                    ControlOutcome.NoActiveSlot,
                    $"Every active-spec slot of repository {spec.RepositoryId} is taken; retry spec run '{specRunId}' when an active spec finishes or raise MaxActiveSpecsPerRepo.");
            }

            spec.MaxActiveSpecsSlot = slot;
        }

        journal.MoveSpec(spec, target);
        journal.Record(ControlAction.Retry, spec.Id, null, target.ToString());
        return await SaveAsync(specRunId, cancellationToken) ? ControlResult.Applied() : Conflict(specRunId);
    }

    public async Task<ControlResult> AbortAsync(RunId specRunId, CancellationToken cancellationToken)
    {
        if (await specRuns.GetAsync(specRunId, cancellationToken) is not { } spec)
        {
            return NotFound(specRunId);
        }

        if (spec.IsTerminal)
        {
            return ControlResult.NotAllowed($"Spec run '{specRunId}' is already {spec.Status}.");
        }

        TicketRun[] openTickets = (await ticketRuns.ListBySpecRunAsync(specRunId, cancellationToken)).Where(ticket => !ticket.IsTerminal).ToArray();
        StepRun[] activeSteps = (await stepRuns.ListBySpecRunAsync(specRunId, cancellationToken)).Where(step => step.IsActive).ToArray();
        TestLease? lease = await leases.FindActiveAsync(specRunId, cancellationToken);

        journal.MoveSpec(spec, SpecRunStatus.Aborted);
        foreach (TicketRun ticket in openTickets)
        {
            journal.MoveTicket(ticket, TicketRunStatus.Aborted);
        }

        foreach (StepRun step in activeSteps)
        {
            journal.CancelStep(step);
        }

        lease?.Release(journal.Now);
        journal.Record(ControlAction.Abort, spec.Id, null, nameof(SpecRunStatus.Aborted), openTickets.Select(ticket => ticket.Id));
        if (!await SaveAsync(specRunId, cancellationToken))
        {
            return Conflict(specRunId);
        }

        // Worktrees are cleaned up by the completion handler reacting to the Aborted status change.
        return ControlResult.Applied(await stopper.StopAsync(activeSteps, lease));
    }

    private async Task<bool> SaveAsync(RunId specRunId, CancellationToken cancellationToken) =>
        await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved;

    private static ControlResult NotFound(RunId specRunId) => ControlResult.NotFound($"Spec run '{specRunId}' does not exist.");

    private static ControlResult Conflict(RunId specRunId) =>
        ControlResult.ConcurrencyConflict($"Spec run '{specRunId}' was changed concurrently; reload it and try again.");
}
