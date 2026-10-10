using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Attention;

/// <summary>
/// Event-bus subscriber that starts the resolution pipeline whenever a run or ticket enters <c>NeedsAttention</c>. The
/// (startup and periodic) <see cref="FrontierReconciliationRequested"/> also relaunches the pipeline for items still waiting
/// for their automatic fix, e.g. after a restart that interrupted it; launching is idempotent per item.
/// </summary>
public sealed class AttentionTriageEventHandler(IAttentionTriageLauncher launcher, ISpecRunRepository specRuns, ITicketRunRepository ticketRuns)
{
    public async Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        switch (envelope.Event)
        {
            case TicketRunStatusChanged { To: TicketRunStatus.NeedsAttention } ticket:
                launcher.Launch(new AttentionTriageAssignment(ticket.SpecRunId, ticket.TicketRunId));
                break;
            case SpecRunStatusChanged { To: SpecRunStatus.NeedsAttention } spec:
                launcher.Launch(new AttentionTriageAssignment(spec.SpecRunId, null));
                break;
            case FrontierReconciliationRequested requested:
                await RelaunchPendingAsync(requested.SpecRunId, cancellationToken);
                break;
        }
    }

    private async Task RelaunchPendingAsync(RunId specRunId, CancellationToken cancellationToken)
    {
        if (await specRuns.GetAsync(specRunId, cancellationToken) is { Status: SpecRunStatus.NeedsAttention, Attention.AutoFixPending: true })
        {
            launcher.Launch(new AttentionTriageAssignment(specRunId, null));
        }

        foreach (TicketRun ticket in await ticketRuns.ListBySpecRunAsync(specRunId, cancellationToken))
        {
            if (ticket is { Status: TicketRunStatus.NeedsAttention, Attention.AutoFixPending: true })
            {
                launcher.Launch(new AttentionTriageAssignment(specRunId, ticket.Id));
            }
        }
    }
}
