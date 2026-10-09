using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.ReviewLoop;

/// <summary>
/// Event-bus subscriber that starts review loops: a freshly implemented ticket entering <c>Reviewing</c> is reviewed, and
/// when an implementer slot frees, tickets whose findings wait for a fix turn are launched again. A ticket coming back
/// from a fix is not launched here; the loop that ran the fix continues with the next review round itself.
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
        if (envelope.Event is not TicketRunStatusChanged changed)
        {
            return;
        }

        if (changed is { From: TicketRunStatus.Implementing, To: TicketRunStatus.Reviewing })
        {
            launcher.Launch(new ReviewAssignment(changed.SpecRunId, changed.TicketRunId));
        }

        if (ImplementerCapacity.Occupies(changed.From) && !ImplementerCapacity.Occupies(changed.To))
        {
            await LaunchTicketsAwaitingFixAsync(cancellationToken);
        }
    }

    private async Task LaunchTicketsAwaitingFixAsync(CancellationToken cancellationToken)
    {
        foreach (SpecRun specRun in await specRuns.ListNonTerminalAsync(cancellationToken))
        {
            foreach (TicketRun ticket in await ticketRuns.ListBySpecRunAsync(specRun.Id, cancellationToken))
            {
                if (ticket.Status == TicketRunStatus.Reviewing && await AwaitsFixAsync(ticket, cancellationToken))
                {
                    launcher.Launch(new ReviewAssignment(specRun.Id, ticket.Id));
                }
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
