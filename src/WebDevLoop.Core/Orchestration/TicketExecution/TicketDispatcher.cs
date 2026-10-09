using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.TicketExecution;

/// <summary>
/// Claims dispatchable tickets (<see cref="TicketRunStatus.Ready"/> → <see cref="TicketRunStatus.Implementing"/>) up to
/// the free global/per-repo implementer capacity and launches their implementers in the background. Each claim is a
/// separate compare-and-swap save, so concurrent dispatchers or duplicate events claim a ticket at most once.
/// </summary>
public sealed class TicketDispatcher(
    IEffectiveSettingsProvider settings,
    ImplementerCapacity capacity,
    ImplementerCapacityGate gate,
    IImplementationLauncher launcher,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    /// <param name="candidates">Dispatchable tickets of <paramref name="specRun"/> in dispatch order.</param>
    public async Task<DispatchResult> DispatchAsync(SpecRun specRun, IReadOnlyList<TicketRun> candidates, CancellationToken cancellationToken)
    {
        TicketRun[] ready = candidates.Where(ticket => ticket.Status == TicketRunStatus.Ready).ToArray();
        if (ready.Length == 0)
        {
            return DispatchResult.Nothing;
        }

        EffectiveSettings effective = await settings.GetAsync(specRun.RepositoryId, cancellationToken);
        var claimed = new List<TicketRunId>();
        bool conflict = false;
        using (await gate.EnterAsync(cancellationToken))
        {
            int available = await capacity.GetAvailableAsync(specRun.RepositoryId, effective, cancellationToken);
            foreach (TicketRun ticket in ready.Take(available))
            {
                Claim(ticket, effective.WorkspaceRootDirectory);
                if (await unitOfWork.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
                {
                    conflict = true;
                    break;
                }

                claimed.Add(ticket.Id);
            }
        }

        foreach (TicketRunId ticketId in claimed)
        {
            launcher.Launch(new ImplementationAssignment(specRun.Id, ticketId));
        }

        return new DispatchResult(claimed, conflict);
    }

    private void Claim(TicketRun ticket, string workspaceRoot)
    {
        DateTimeOffset now = clock.UtcNow;
        ticket.TransitionTo(TicketRunStatus.Implementing, now);
        ticket.WorktreePath = TicketWorktreeLayout.PathFor(workspaceRoot, ticket.SpecRunId, ticket.Id);
        outbox.Append(new TicketRunStatusChanged(ticket.SpecRunId, ticket.Id, TicketRunStatus.Ready, TicketRunStatus.Implementing, now));
    }
}
