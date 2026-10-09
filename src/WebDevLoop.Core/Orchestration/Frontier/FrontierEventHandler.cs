using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Orchestration.Frontier;

/// <summary>
/// Event-bus subscriber that keeps the frontier continuous: a run that starts running, an explicit reconciliation
/// request, an integrated or retried ticket recompute that run's frontier; a ticket leaving an implementer slot frees
/// capacity that any running spec may use, so all running specs are reconciled.
/// </summary>
public sealed class FrontierEventHandler(FrontierService frontier)
{
    public async Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        switch (envelope.Event)
        {
            case FrontierReconciliationRequested requested:
                await frontier.ReconcileAsync(requested.SpecRunId, cancellationToken);
                break;
            case SpecRunStatusChanged { To: SpecRunStatus.Running } started:
                await frontier.ReconcileAsync(started.SpecRunId, cancellationToken);
                break;
            case TicketRunStatusChanged changed when ImplementerCapacity.Occupies(changed.From) && !ImplementerCapacity.Occupies(changed.To):
                await frontier.ReconcileAllRunningAsync(cancellationToken);
                break;
            case TicketRunStatusChanged changed when RecomputesOwnFrontier(changed):
                await frontier.ReconcileAsync(changed.SpecRunId, cancellationToken);
                break;
        }
    }

    /// <summary>An integrated ticket may unblock dependents; a ticket retried from NeedsAttention is dispatchable again.</summary>
    private static bool RecomputesOwnFrontier(TicketRunStatusChanged changed) =>
        changed.To == TicketRunStatus.Integrated
        || (changed.From == TicketRunStatus.NeedsAttention && changed.To == TicketRunStatus.Ready);
}
