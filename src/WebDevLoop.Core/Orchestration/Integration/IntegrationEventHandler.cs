using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Integration;

/// <summary>
/// Event-bus subscriber that starts integration sagas: a ticket entering <c>Integrating</c> is launched, and when a ticket
/// becomes <c>Integrated</c> the spec's other <c>Integrating</c> tickets are launched again, since they may have been
/// waiting for that layer (see <see cref="IntegrationOutcome.WaitingForEarlierLayer"/>).
/// </summary>
public sealed class IntegrationEventHandler(IIntegrationLauncher launcher, ISpecRunRepository specRuns, ITicketRunRepository ticketRuns)
{
    public async Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Event is not TicketRunStatusChanged { To: TicketRunStatus.Integrating or TicketRunStatus.Integrated } changed
            || await specRuns.GetAsync(changed.SpecRunId, cancellationToken) is not { } spec)
        {
            return;
        }

        if (changed.To == TicketRunStatus.Integrating)
        {
            launcher.Launch(new IntegrationAssignment(spec.RepositoryId, spec.Id, changed.TicketRunId));
            return;
        }

        foreach (TicketRun ticket in await ticketRuns.ListBySpecRunAsync(spec.Id, cancellationToken))
        {
            if (ticket.Status == TicketRunStatus.Integrating && ticket.Id != changed.TicketRunId)
            {
                launcher.Launch(new IntegrationAssignment(spec.RepositoryId, spec.Id, ticket.Id));
            }
        }
    }
}
