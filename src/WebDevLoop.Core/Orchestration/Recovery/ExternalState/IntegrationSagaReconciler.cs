using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Integration;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Recovery.ExternalState;

/// <summary>
/// Resumes unfinished integration sagas from their last durable checkpoint (workflow step 7). The saga itself reconciles
/// every replayed side effect against Git and GitHub (compare-and-swap ref updates, leased pushes, PR lookup by exact head
/// ref, stack membership, issue state), so resuming is idempotent; this step adds what only recovery needs:
/// <list type="bullet">
/// <item>A needs-attention ticket whose squash commit already moved the integration branch blocks every later layer of
/// its spec, so once <see cref="ExternalReconciliationOptions.ParkedIntegrationRetryInterval"/> passed since its saga last
/// changed, it moves back to <c>Integrating</c> and its saga is resumed.</item>
/// <item>A PR found on the ticket's run-scoped stack branch is only adopted when its body identifies this run and ticket;
/// otherwise the ticket needs attention instead of publishing someone else's PR as its layer.</item>
/// <item>Sagas past the squash are advanced right away (oldest first, so layers stay in integration order); tickets whose
/// saga has not squashed yet may need the conflict resolver agent and are handed to the background launcher.</item>
/// </list>
/// </summary>
public sealed class IntegrationSagaReconciler(
    ITicketRunRepository ticketRuns,
    IIntegrationSagaRepository sagas,
    IGitHubPullsAndStacks pulls,
    IntegrationSagaRunner runner,
    IIntegrationLauncher launcher,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock,
    ExternalReconciliationOptions options) : ISpecReconciliationStep
{
    public async Task<IReadOnlyList<ReconciliationAction>> ReconcileAsync(SpecReconciliationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var actions = new List<ReconciliationAction>();
        await ResumeParkedAsync(context.Spec, actions, cancellationToken);

        var integrating = new List<(TicketRun Ticket, IntegrationSaga? Saga)>();
        foreach (TicketRun ticket in await ticketRuns.ListBySpecRunAsync(context.Spec.Id, cancellationToken))
        {
            if (ticket.Status == TicketRunStatus.Integrating)
            {
                integrating.Add((ticket, await sagas.FindLatestForTicketAsync(ticket.Id, cancellationToken)));
            }
        }

        foreach ((TicketRun ticket, IntegrationSaga? saga) in integrating
            .OrderBy(entry => entry.Saga?.CreatedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(entry => entry.Ticket.Id.Value, StringComparer.Ordinal))
        {
            // An earlier saga's run may already have finished this ticket's layer.
            if (ticket.Status == TicketRunStatus.Integrating)
            {
                actions.Add(await ResumeAsync(context, ticket, saga, cancellationToken));
            }
        }

        return actions;
    }

    private async Task ResumeParkedAsync(SpecRun spec, List<ReconciliationAction> actions, CancellationToken cancellationToken)
    {
        foreach (TicketRun ticket in await ticketRuns.ListBySpecRunAsync(spec.Id, cancellationToken))
        {
            if (ticket.Status != TicketRunStatus.NeedsAttention
                || await sagas.FindLatestForTicketAsync(ticket.Id, cancellationToken) is not { IsCompleted: false, Checkpoint: >= IntegrationSagaCheckpoint.IntegrationRefUpdated } saga
                || clock.UtcNow - saga.UpdatedAt < options.ParkedIntegrationRetryInterval)
            {
                continue;
            }

            DateTimeOffset now = clock.UtcNow;
            string reason = ticket.FailureReason ?? "(no reason recorded)";
            ticket.TransitionTo(TicketRunStatus.Integrating, now);
            outbox.Append(new TicketRunStatusChanged(spec.Id, ticket.Id, TicketRunStatus.NeedsAttention, TicketRunStatus.Integrating, now));
            if (await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved)
            {
                actions.Add(new ReconciliationAction(spec.Id, ticket.Id, ReconciliationActionKind.ParkedIntegrationResumed, $"Resuming at {saga.Checkpoint}; parked because: {reason}"));
            }
        }
    }

    private async Task<ReconciliationAction> ResumeAsync(
        SpecReconciliationContext context,
        TicketRun ticket,
        IntegrationSaga? saga,
        CancellationToken cancellationToken)
    {
        SpecRun spec = context.Spec;
        var assignment = new IntegrationAssignment(spec.RepositoryId, spec.Id, ticket.Id);
        if (saga is not { IsCompleted: false, Checkpoint: > IntegrationSagaCheckpoint.Started })
        {
            launcher.Launch(assignment);
            return new ReconciliationAction(spec.Id, ticket.Id, ReconciliationActionKind.IntegrationLaunched, saga is null ? "No saga yet." : $"Saga at {saga.Checkpoint}.");
        }

        if (await FindForeignPullRequestAsync(context, ticket, saga, cancellationToken) is { } foreign)
        {
            return await RejectAsync(spec, ticket, saga, foreign, cancellationToken);
        }

        IntegrationSagaCheckpoint from = saga.Checkpoint;
        IntegrationResult result = await runner.RunAsync(assignment, cancellationToken);
        return new ReconciliationAction(
            spec.Id, ticket.Id, ReconciliationActionKind.IntegrationResumed, $"From {from}: {result.Outcome}{(result.Reason is null ? string.Empty : $" ({result.Reason})")}");
    }

    /// <summary>A PR on the ticket's stack branch that the saga has not recorded yet must identify this run and ticket.</summary>
    private async Task<PullRequestSnapshot?> FindForeignPullRequestAsync(
        SpecReconciliationContext context,
        TicketRun ticket,
        IntegrationSaga saga,
        CancellationToken cancellationToken)
    {
        if (saga.Checkpoint != IntegrationSagaCheckpoint.StackBranchPushed || saga.PullRequestNumber is not null)
        {
            return null;
        }

        PullRequestSnapshot? pull = await pulls.FindPullRequestByHeadAsync(context.Repository.Ref, saga.StackBranchName, cancellationToken);
        return pull is null || PullRequestIdentity.Identifies(pull.Body, context.Spec.Id, ticket.Id) ? null : pull;
    }

    private async Task<ReconciliationAction> RejectAsync(
        SpecRun spec,
        TicketRun ticket,
        IntegrationSaga saga,
        PullRequestSnapshot foreign,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;
        string reason = $"Pull request #{foreign.Number} on the run-scoped stack branch '{saga.StackBranchName}' does not identify run '{spec.Id}' "
            + $"and ticket run '{ticket.Id}', so it was not adopted as the ticket's stack layer.";
        saga.RecordError(reason, now);
        ticket.MarkNeedsAttention(reason, now);
        outbox.Append(new TicketRunStatusChanged(spec.Id, ticket.Id, TicketRunStatus.Integrating, TicketRunStatus.NeedsAttention, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new ReconciliationAction(spec.Id, ticket.Id, ReconciliationActionKind.ForeignPullRequestRejected, reason);
    }
}
