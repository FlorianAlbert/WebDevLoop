using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Frontier;

/// <summary>
/// Recomputes a running spec's ticket frontier (workflow step 8): Blocked tickets whose blockers are all integrated
/// become Ready, and dispatchable tickets are claimed and launched while other implementers keep running. Idempotent,
/// so duplicate events, periodic reconciliation, and recovery may all call it.
/// </summary>
public sealed class FrontierService(
    ISpecRunRepository specRuns,
    ITicketRunRepository ticketRuns,
    TicketDispatcher dispatcher,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    /// <summary>A lost race is recomputed from fresh state right away instead of waiting for periodic reconciliation.</summary>
    private const int MaxAttempts = 3;

    public async Task<FrontierResult> ReconcileAsync(RunId specRunId, CancellationToken cancellationToken)
    {
        var unblocked = new List<TicketRunId>();
        var dispatched = new List<TicketRunId>();
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            FrontierResult pass = await ReconcileOnceAsync(specRunId, cancellationToken);
            unblocked.AddRange(pass.Unblocked);
            dispatched.AddRange(pass.Dispatched);
            if (pass.Outcome != FrontierOutcome.ConcurrencyConflict)
            {
                return pass with { Unblocked = unblocked, Dispatched = dispatched };
            }
        }

        return new FrontierResult(FrontierOutcome.ConcurrencyConflict, unblocked, dispatched);
    }

    /// <summary>Reconciles every running spec, e.g. after an implementer slot was freed somewhere.</summary>
    public async Task<IReadOnlyList<FrontierResult>> ReconcileAllRunningAsync(CancellationToken cancellationToken)
    {
        RunId[] running = (await specRuns.ListNonTerminalAsync(cancellationToken))
            .Where(run => run.Status == SpecRunStatus.Running)
            .OrderBy(run => run.RepositoryId)
            .ThenBy(run => run.QueuePosition)
            .Select(run => run.Id)
            .ToArray();
        var results = new List<FrontierResult>(running.Length);
        foreach (RunId specRunId in running)
        {
            results.Add(await ReconcileAsync(specRunId, cancellationToken));
        }

        return results;
    }

    private async Task<FrontierResult> ReconcileOnceAsync(RunId specRunId, CancellationToken cancellationToken)
    {
        SpecRun? specRun = await specRuns.GetAsync(specRunId, cancellationToken);
        if (specRun is not { Status: SpecRunStatus.Running })
        {
            return FrontierResult.NotRunning;
        }

        IReadOnlyList<TicketRun> tickets = await ticketRuns.ListBySpecRunAsync(specRunId, cancellationToken);
        IReadOnlyList<TicketDependency> dependencies = await ticketRuns.ListDependenciesAsync(specRunId, cancellationToken);
        FrontierSnapshot snapshot = TicketFrontier.Compute(tickets, dependencies);
        Dictionary<TicketRunId, TicketRun> byId = tickets.ToDictionary(ticket => ticket.Id);

        if (snapshot.Unblocked.Count > 0)
        {
            DateTimeOffset now = clock.UtcNow;
            foreach (TicketRunId ticketId in snapshot.Unblocked)
            {
                byId[ticketId].TransitionTo(TicketRunStatus.Ready, now);
                outbox.Append(new TicketRunStatusChanged(specRunId, ticketId, TicketRunStatus.Blocked, TicketRunStatus.Ready, now));
            }

            if (await unitOfWork.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
            {
                return new FrontierResult(FrontierOutcome.ConcurrencyConflict, [], []);
            }
        }

        TicketRun[] candidates = snapshot.Dispatchable.Select(ticketId => byId[ticketId]).ToArray();
        DispatchResult dispatch = await dispatcher.DispatchAsync(specRun, candidates, cancellationToken);
        FrontierOutcome outcome = dispatch.ConcurrencyConflict ? FrontierOutcome.ConcurrencyConflict : FrontierOutcome.Reconciled;
        return new FrontierResult(outcome, snapshot.Unblocked, dispatch.Dispatched);
    }
}
