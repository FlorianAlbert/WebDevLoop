using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Recovery.Startup;

/// <summary>
/// Recovery nudge: durably asks for a frontier recomputation of every non-terminal spec run, so work cannot stall because
/// an in-process event was missed. Raised by every recovery cycle (startup and periodic).
/// </summary>
public sealed class FrontierReconciliationSignal(ISpecRunRepository specRuns, IOutbox outbox, IUnitOfWork unitOfWork, IClock clock)
    : IFrontierReconciliationTrigger
{
    /// <summary>Returns the number of runs a reconciliation was requested for.</summary>
    public async Task<int> RaiseAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SpecRun> active = await specRuns.ListNonTerminalAsync(cancellationToken);
        if (active.Count == 0)
        {
            return 0;
        }

        DateTimeOffset now = clock.UtcNow;
        foreach (SpecRun specRun in active)
        {
            outbox.Append(new FrontierReconciliationRequested(specRun.Id, now));
        }

        SaveOutcome outcome = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (outcome != SaveOutcome.Saved)
        {
            throw new InvalidOperationException($"Saving reconciliation requests failed: {outcome}.");
        }

        return active.Count;
    }
}
