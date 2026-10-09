using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Completion.ParentReview;

public enum ParentReviewStartOutcome
{
    /// <summary>The spec moved from <c>Running</c> to <c>ParentReviewing</c>, starting the next review cycle.</summary>
    Started,

    /// <summary>The spec is not <c>Running</c> (e.g. already in parent review); nothing was done.</summary>
    NotRunning,

    /// <summary>Some ticket is not done yet (integrated, skipped, or aborted).</summary>
    TicketsOutstanding,

    /// <summary>Another writer changed the spec first (e.g. a duplicate start); it wins.</summary>
    ConcurrencyConflict,
}

/// <summary>
/// Workflow step 9 trigger: once every ticket of a running spec is complete (<c>Integrated</c>, or <c>Skipped</c>/<c>Aborted</c>
/// by the user), the spec moves to <c>ParentReviewing</c>, which counts the next parent-review cycle. Idempotent and race-safe
/// (compare-and-swap on the spec), so duplicate events and recovery may all call it.
/// </summary>
public sealed class ParentReviewStarter(ISpecRunRepository specRuns, ITicketRunRepository ticketRuns, IOutbox outbox, IUnitOfWork unitOfWork, IClock clock)
{
    private readonly SpecRunJournal _journal = new(outbox, clock);

    public async Task<ParentReviewStartOutcome> StartIfTicketsCompleteAsync(RunId specRunId, CancellationToken cancellationToken)
    {
        SpecRun? spec = await specRuns.GetAsync(specRunId, cancellationToken);
        if (spec is not { Status: SpecRunStatus.Running })
        {
            return ParentReviewStartOutcome.NotRunning;
        }

        IReadOnlyList<TicketRun> tickets = await ticketRuns.ListBySpecRunAsync(specRunId, cancellationToken);
        if (!tickets.All(IsComplete))
        {
            return ParentReviewStartOutcome.TicketsOutstanding;
        }

        _journal.Move(spec, SpecRunStatus.ParentReviewing);
        return await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved
            ? ParentReviewStartOutcome.Started
            : ParentReviewStartOutcome.ConcurrencyConflict;
    }

    private static bool IsComplete(TicketRun ticket) => ticket.IsTerminal;
}
