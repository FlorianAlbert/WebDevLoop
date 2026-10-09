using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.Integration;

/// <summary>
/// Workflow step 7 for one ticket in <c>Integrating</c> (entry point for <see cref="IIntegrationLauncher"/> and recovery).
/// Merges are serialized per repository: inside the merge lock the runner loads fresh state, first finishes any earlier
/// ticket of the spec that already moved the integration branch (so layers stay linear in integration order), then starts
/// or resumes this ticket's checkpointed saga (see <see cref="IntegrationSagaSteps"/>).
/// </summary>
public sealed class IntegrationSagaRunner(
    IRepositoryRecordRepository repositories,
    ISpecRunRepository specRuns,
    ITicketRunRepository ticketRuns,
    IStepRunRepository stepRuns,
    IIntegrationSagaRepository sagas,
    IEffectiveSettingsProvider settings,
    RepositoryIntegrationGate gate,
    IntegrationSagaSteps steps,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<IntegrationResult> RunAsync(IntegrationAssignment assignment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        using (await gate.EnterAsync(assignment.RepositoryId, cancellationToken))
        {
            TicketRun? ticket = await ticketRuns.GetAsync(assignment.TicketRunId, cancellationToken);
            if (ticket is not { Status: TicketRunStatus.Integrating })
            {
                return IntegrationResult.NotIntegrating;
            }

            IReadOnlyList<StepRun> ticketSteps = await stepRuns.ListByTicketRunAsync(ticket.Id, cancellationToken);
            if (ticketSteps.Any(step => step.IsActive))
            {
                return IntegrationResult.AlreadyRunning;
            }

            SpecRun? spec = await specRuns.GetAsync(ticket.SpecRunId, cancellationToken);
            RepositoryRecord? repository = spec is null ? null : await repositories.GetAsync(spec.RepositoryId, cancellationToken);
            if (spec is null || repository is null || spec.RepositoryId != assignment.RepositoryId || spec.IntegrationTipSha is not { } tip)
            {
                return await NeedsAttentionAsync(
                    ticket, $"Spec run, repository, or integration tip of ticket '{ticket.Id}' is missing or does not match repository {assignment.RepositoryId}.", cancellationToken);
            }

            EffectiveSettings effective = await settings.GetAsync(spec.RepositoryId, cancellationToken);
            if (await PublishEarlierLayersAsync(spec, ticket, repository, effective, cancellationToken) is { } waiting)
            {
                return waiting;
            }

            IntegrationSaga? saga = await sagas.FindLatestForTicketAsync(ticket.Id, cancellationToken);
            if (saga is null or { IsCompleted: true })
            {
                // Persisted before any side effect, so a crash during the squash resumes this saga instead of starting another.
                saga = IntegrationSaga.Start(spec.Id, ticket.Id, tip, clock.UtcNow);
                sagas.Add(saga);
                outbox.Append(new SagaCheckpointAdvanced(spec.Id, ticket.Id, saga.Checkpoint, clock.UtcNow));
                if (await unitOfWork.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
                {
                    return IntegrationResult.ConcurrencyConflict;
                }
            }

            return await steps.AdvanceAsync(new IntegrationContext(spec, ticket, repository, effective, saga), cancellationToken);
        }
    }

    /// <summary>
    /// Sagas of the same spec that moved the integration branch but have not published their layer must finish first;
    /// otherwise this ticket's layer would be stacked on an unpublished commit.
    /// </summary>
    /// <returns>Null when no earlier layer is pending; otherwise why this ticket waits.</returns>
    private async Task<IntegrationResult?> PublishEarlierLayersAsync(
        SpecRun spec,
        TicketRun ticket,
        RepositoryRecord repository,
        EffectiveSettings effective,
        CancellationToken cancellationToken)
    {
        IntegrationSaga[] earlier = (await sagas.ListIncompleteAsync(cancellationToken))
            .Where(saga => saga.SpecRunId == spec.Id
                && saga.TicketRunId != ticket.Id
                && saga.Checkpoint >= IntegrationSagaCheckpoint.IntegrationRefUpdated)
            .OrderBy(saga => saga.CreatedAt)
            .ThenBy(saga => saga.Id)
            .ToArray();
        foreach (IntegrationSaga saga in earlier)
        {
            TicketRun? owner = await ticketRuns.GetAsync(saga.TicketRunId, cancellationToken);
            IntegrationResult result = owner is { Status: TicketRunStatus.Integrating }
                ? await steps.AdvanceAsync(new IntegrationContext(spec, owner, repository, effective, saga), cancellationToken)
                : new IntegrationResult(IntegrationOutcome.NeedsAttention, saga.LastError);
            if (result.Outcome != IntegrationOutcome.Integrated)
            {
                return new IntegrationResult(
                    IntegrationOutcome.WaitingForEarlierLayer,
                    $"Ticket '{saga.TicketRunId}' moved the integration branch but its stack layer is not published ({saga.Checkpoint}): {result.Reason ?? result.Outcome.ToString()}");
            }
        }

        return null;
    }

    private async Task<IntegrationResult> NeedsAttentionAsync(TicketRun ticket, string reason, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;
        TicketRunStatus previous = ticket.Status;
        ticket.MarkNeedsAttention(reason, now);
        outbox.Append(new TicketRunStatusChanged(ticket.SpecRunId, ticket.Id, previous, TicketRunStatus.NeedsAttention, now));
        return await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved
            ? new IntegrationResult(IntegrationOutcome.NeedsAttention, reason)
            : IntegrationResult.ConcurrencyConflict;
    }
}
