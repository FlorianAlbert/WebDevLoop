using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.ReviewLoop;

/// <summary>
/// Event-bus subscriber that starts review loops: a freshly implemented ticket entering <c>Reviewing</c>, or a ticket the
/// user retried into <c>Reviewing</c> from <c>NeedsAttention</c>, is reviewed, and
/// when an implementer slot frees, tickets whose findings wait for a fix turn are launched again. A ticket coming back
/// from a fix is not launched here; the loop that ran the fix continues with the next review round itself. A (periodic or
/// startup) <see cref="FrontierReconciliationRequested"/> relaunches every <c>Reviewing</c> ticket of the run without an
/// active step: loops waiting for a slot whose release was missed, and loops that died between rounds. Relaunching is
/// safe: each review round and fix turn is claimed once.
/// </summary>
public sealed class ReviewLoopEventHandler(
    IReviewLoopLauncher launcher,
    ISpecRunRepository specRuns,
    ITicketRunRepository ticketRuns,
    IStepRunRepository stepRuns)
{
    public async Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        switch (envelope.Event)
        {
            case FrontierReconciliationRequested requested:
                await LaunchIdleReviewLoopsAsync(requested.SpecRunId, cancellationToken);
                break;
            case TicketRunStatusChanged changed:
                if (changed is { From: TicketRunStatus.Implementing or TicketRunStatus.NeedsAttention, To: TicketRunStatus.Reviewing })
                {
                    launcher.Launch(new ReviewAssignment(changed.SpecRunId, changed.TicketRunId));
                }

                if (ImplementerCapacity.Occupies(changed.From) && !ImplementerCapacity.Occupies(changed.To))
                {
                    foreach (SpecRun specRun in await specRuns.ListNonTerminalAsync(cancellationToken))
                    {
                        await LaunchTicketsAwaitingFixAsync(specRun.Id, cancellationToken);
                    }
                }

                break;
        }
    }

    private async Task LaunchTicketsAwaitingFixAsync(RunId specRunId, CancellationToken cancellationToken)
    {
        foreach (TicketRun ticket in await ticketRuns.ListBySpecRunAsync(specRunId, cancellationToken))
        {
            if (ticket.Status == TicketRunStatus.Reviewing && await AwaitsFixAsync(ticket, cancellationToken))
            {
                launcher.Launch(new ReviewAssignment(specRunId, ticket.Id));
            }
        }
    }

    private async Task LaunchIdleReviewLoopsAsync(RunId specRunId, CancellationToken cancellationToken)
    {
        foreach (TicketRun ticket in await ticketRuns.ListBySpecRunAsync(specRunId, cancellationToken))
        {
            if (ticket.Status == TicketRunStatus.Reviewing
                && !(await stepRuns.ListByTicketRunAsync(ticket.Id, cancellationToken)).Any(step => step.IsActive))
            {
                launcher.Launch(new ReviewAssignment(specRunId, ticket.Id));
            }
        }
    }

    /// <summary>The current round is complete on both axes, found issues, and nothing runs for the ticket.</summary>
    private async Task<bool> AwaitsFixAsync(TicketRun ticket, CancellationToken cancellationToken)
    {
        IReadOnlyList<StepRun> steps = await stepRuns.ListByTicketRunAsync(ticket.Id, cancellationToken);
        if (steps.Any(step => step.IsActive))
        {
            return false;
        }

        IReadOnlyDictionary<FindingAxis, ReviewReport> reports = ReviewStepResults.ReadCurrentRound(ticket, steps);
        return reports.Count == ReviewRequest.BothAxes.Count && reports.Values.Any(report => report.Findings.Count > 0);
    }
}
