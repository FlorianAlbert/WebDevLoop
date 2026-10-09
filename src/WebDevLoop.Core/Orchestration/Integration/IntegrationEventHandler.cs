using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Integration;

/// <summary>
/// Event-bus subscriber that starts integration sagas: a ticket entering <c>Integrating</c> is launched, and when a ticket
/// becomes <c>Integrated</c> the spec's other <c>Integrating</c> tickets are launched again, since they may have been
/// waiting for that layer (see <see cref="IntegrationOutcome.WaitingForEarlierLayer"/>). A (periodic or startup)
/// <see cref="FrontierReconciliationRequested"/> relaunches every <c>Integrating</c> ticket of the run, which resumes sagas
/// that faulted or lost a race; relaunching is idempotent because sagas resume from their persisted checkpoint.
/// </summary>
public sealed class IntegrationEventHandler(IIntegrationLauncher launcher, ISpecRunRepository specRuns, ITicketRunRepository ticketRuns)
{
    public async Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        switch (envelope.Event)
        {
            case FrontierReconciliationRequested requested when await specRuns.GetAsync(requested.SpecRunId, cancellationToken) is { } spec:
                await LaunchIntegratingAsync(spec, except: null, cancellationToken);
                break;
            case TicketRunStatusChanged { To: TicketRunStatus.Integrating } changed when await specRuns.GetAsync(changed.SpecRunId, cancellationToken) is { } spec:
                launcher.Launch(new IntegrationAssignment(spec.RepositoryId, spec.Id, changed.TicketRunId));
                break;
            case TicketRunStatusChanged { To: TicketRunStatus.Integrated } changed when await specRuns.GetAsync(changed.SpecRunId, cancellationToken) is { } spec:
                await LaunchIntegratingAsync(spec, changed.TicketRunId, cancellationToken);
                break;
        }
    }

    private async Task LaunchIntegratingAsync(SpecRun spec, TicketRunId? except, CancellationToken cancellationToken)
    {
        foreach (TicketRun ticket in await ticketRuns.ListBySpecRunAsync(spec.Id, cancellationToken))
        {
            if (ticket.Status == TicketRunStatus.Integrating && ticket.Id != except)
            {
                launcher.Launch(new IntegrationAssignment(spec.RepositoryId, spec.Id, ticket.Id));
            }
        }
    }
}
