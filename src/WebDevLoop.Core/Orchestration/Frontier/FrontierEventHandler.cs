using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Frontier;

/// <summary>
/// Event-bus subscriber that keeps the frontier continuous: a run that starts running, an explicit reconciliation
/// request, an integrated, skipped, or retried ticket recompute that run's frontier; a ticket leaving an implementer slot frees
/// capacity that any running spec may use, so all running specs are reconciled.
/// </summary>
public sealed class FrontierEventHandler(FrontierService frontier)
{
    public async Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        IReadOnlyList<FrontierResult> results = envelope.Event switch
        {
            FrontierReconciliationRequested requested => [await frontier.ReconcileAsync(requested.SpecRunId, cancellationToken)],
            SpecRunStatusChanged { To: SpecRunStatus.Running } started => [await frontier.ReconcileAsync(started.SpecRunId, cancellationToken)],
            TicketRunStatusChanged changed when ImplementerCapacity.Occupies(changed.From) && !ImplementerCapacity.Occupies(changed.To) =>
                await frontier.ReconcileAllRunningAsync(cancellationToken),
            TicketRunStatusChanged changed when RecomputesOwnFrontier(changed) => [await frontier.ReconcileAsync(changed.SpecRunId, cancellationToken)],
            _ => [],
        };
        GitHubIssuesException[] lookupErrors = results.SelectMany(result => result.LookupErrors).ToArray();
        if (lookupErrors.Length > 0)
        {
            // Report through the event bus's logging boundary only after all unrelated work has been reconciled.
            throw new AggregateException("Some ticket blocker checks failed; affected tickets were deferred for normal recovery. Other tickets and specs were still reconciled.", lookupErrors);
        }
    }

    /// <summary>Local completion or retry requests a fresh GitHub blocker check; local status alone never authorizes dispatch.</summary>
    private static bool RecomputesOwnFrontier(TicketRunStatusChanged changed) =>
        changed.To.IsLocallyComplete()
        || (changed.From == TicketRunStatus.NeedsAttention && changed.To == TicketRunStatus.Ready);
}
