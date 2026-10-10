using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Integration;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Control;

/// <summary>Retry, Skip, and Abort of a ticket run (see <see cref="IRunControl"/>).</summary>
public sealed class TicketRunControl(
    ISpecRunRepository specRuns,
    ITicketRunRepository ticketRuns,
    IStepRunRepository stepRuns,
    IIntegrationSagaRepository sagas,
    RepositoryIntegrationGate gate,
    ActiveWorkStopper stopper,
    RunControlJournal journal,
    IUnitOfWork unitOfWork,
    RunControlOptions options)
{
    /// <param name="automatic">WebDevLoop's own retry after a successful remediation rather than the user's command; audited as such.</param>
    /// <param name="resumeAt">Where the remediation wants the ticket resumed; null resumes the phase that failed.</param>
    public async Task<ControlResult> RetryAsync(
        TicketRunId ticketRunId,
        CancellationToken cancellationToken,
        bool automatic = false,
        TicketRunStatus? resumeAt = null)
    {
        (TicketRun? ticket, SpecRun? spec, ControlResult? refused) = await LoadAsync(ticketRunId, cancellationToken);
        if (refused is not null)
        {
            return refused;
        }

        if (ticket!.Status != TicketRunStatus.NeedsAttention)
        {
            return ControlResult.NotAllowed($"Ticket run '{ticketRunId}' is {ticket.Status}; only a ticket run that needs attention can be retried.");
        }

        // A saga past the squash must be resumed: implementing again would leave its commit orphaned on the integration branch.
        bool integrationInProgress = await sagas.FindLatestForTicketAsync(ticket.Id, cancellationToken)
            is { IsCompleted: false, Checkpoint: >= IntegrationSagaCheckpoint.SquashCommitCreated };
        TicketRunStatus target = journal.RetryTicket(ticket, integrationInProgress, resumeAt);
        journal.Record(automatic ? ControlAction.AutoRetry : ControlAction.Retry, spec!.Id, ticket.Id, target.ToString());
        return await SaveAsync(ticketRunId, cancellationToken);
    }

    public async Task<ControlResult> SkipAsync(
        TicketRunId ticketRunId,
        SkipDependents dependents,
        CancellationToken cancellationToken,
        bool automatic = false)
    {
        (TicketRun? ticket, SpecRun? spec, ControlResult? refused) = await LoadAsync(ticketRunId, cancellationToken);
        if (refused is not null)
        {
            return refused;
        }

        if (!IsSkippable(ticket!))
        {
            return ControlResult.NotAllowed(
                $"Ticket run '{ticketRunId}' is {ticket!.Status}; only a ticket run that is not being worked on (Blocked, Ready, NeedsAttention) can be skipped. Abort it instead.");
        }

        List<TicketRun> skipped = [ticket!];
        if (dependents == SkipDependents.Skip)
        {
            skipped.AddRange(await DependentsToSkipAsync(ticket!, cancellationToken));
        }

        foreach (TicketRun candidate in skipped)
        {
            if (await IntegrationBlockerAsync(candidate, cancellationToken) is { } blocker)
            {
                return ControlResult.NotAllowed(blocker);
            }
        }

        foreach (TicketRun candidate in skipped)
        {
            journal.MoveTicket(candidate, TicketRunStatus.Skipped);
        }

        journal.Record(automatic ? ControlAction.AutoSkip : ControlAction.Skip, spec!.Id, ticket!.Id, nameof(TicketRunStatus.Skipped), skipped.Skip(1).Select(candidate => candidate.Id));
        return await SaveAsync(ticketRunId, cancellationToken);
    }

    public async Task<ControlResult> AbortAsync(TicketRunId ticketRunId, CancellationToken cancellationToken)
    {
        (TicketRun? ticket, SpecRun? spec, ControlResult? refused) = await LoadAsync(ticketRunId, cancellationToken);
        if (refused is not null)
        {
            return refused;
        }

        if (ticket!.IsTerminal)
        {
            return ControlResult.NotAllowed($"Ticket run '{ticketRunId}' is already {ticket.Status}.");
        }

        IDisposable? merge = null;
        if (ticket.Status == TicketRunStatus.Integrating)
        {
            // A running saga holds the merge lock; aborting under it means the saga cannot move the branch for an aborted ticket.
            merge = await gate.TryEnterAsync(spec!.RepositoryId, options.IntegrationGateTimeout, cancellationToken);
            if (merge is null)
            {
                return ControlResult.ConcurrencyConflict(
                    $"A merge of repository {spec.RepositoryId} is in progress; try aborting ticket run '{ticketRunId}' again when it finished.");
            }
        }

        StepRun[] activeSteps;
        using (merge)
        {
            if (await IntegrationBlockerAsync(ticket, cancellationToken) is { } blocker)
            {
                return ControlResult.NotAllowed(blocker);
            }

            activeSteps = (await stepRuns.ListByTicketRunAsync(ticket.Id, cancellationToken)).Where(step => step.IsActive).ToArray();
            journal.MoveTicket(ticket, TicketRunStatus.Aborted);
            foreach (StepRun step in activeSteps)
            {
                journal.CancelStep(step);
            }

            journal.Record(ControlAction.Abort, spec!.Id, ticket.Id, nameof(TicketRunStatus.Aborted));
            ControlResult saved = await SaveAsync(ticketRunId, cancellationToken);
            if (!saved.IsApplied)
            {
                return saved;
            }
        }

        return ControlResult.Applied(await stopper.StopAsync(activeSteps, releasedLease: null));
    }

    private static bool IsSkippable(TicketRun ticket) =>
        ticket.Status is TicketRunStatus.Blocked or TicketRunStatus.Ready or TicketRunStatus.NeedsAttention;

    private async Task<(TicketRun? Ticket, SpecRun? Spec, ControlResult? Refused)> LoadAsync(TicketRunId ticketRunId, CancellationToken cancellationToken)
    {
        if (await ticketRuns.GetAsync(ticketRunId, cancellationToken) is not { } ticket)
        {
            return (null, null, ControlResult.NotFound($"Ticket run '{ticketRunId}' does not exist."));
        }

        if (await specRuns.GetAsync(ticket.SpecRunId, cancellationToken) is not { } spec)
        {
            return (null, null, ControlResult.NotFound($"Spec run '{ticket.SpecRunId}' of ticket run '{ticketRunId}' does not exist."));
        }

        return spec.IsTerminal
            ? (ticket, spec, ControlResult.NotAllowed($"Spec run '{spec.Id}' of ticket run '{ticketRunId}' is {spec.Status}."))
            : (ticket, spec, null);
    }

    /// <summary>Not-yet-started tickets that depend on <paramref name="ticket"/>, transitively, in breadth-first order.</summary>
    private async Task<IReadOnlyList<TicketRun>> DependentsToSkipAsync(TicketRun ticket, CancellationToken cancellationToken)
    {
        Dictionary<TicketRunId, TicketRun> byId = (await ticketRuns.ListBySpecRunAsync(ticket.SpecRunId, cancellationToken)).ToDictionary(candidate => candidate.Id);
        ILookup<TicketRunId, TicketRunId> dependentsOf = (await ticketRuns.ListDependenciesAsync(ticket.SpecRunId, cancellationToken))
            .ToLookup(edge => edge.BlockingTicketRunId, edge => edge.BlockedTicketRunId);
        var found = new List<TicketRun>();
        var visited = new HashSet<TicketRunId> { ticket.Id };
        var pending = new Queue<TicketRunId>([ticket.Id]);
        while (pending.TryDequeue(out TicketRunId current))
        {
            foreach (TicketRunId dependentId in dependentsOf[current])
            {
                if (visited.Add(dependentId) && byId.TryGetValue(dependentId, out TicketRun? dependent) && IsSkippable(dependent))
                {
                    found.Add(dependent);
                    pending.Enqueue(dependentId);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// A ticket whose saga already moved the integration branch cannot be given up: its squash commit stays on the branch and
    /// later layers wait for its stack layer to be published.
    /// </summary>
    private async Task<string?> IntegrationBlockerAsync(TicketRun ticket, CancellationToken cancellationToken) =>
        await sagas.FindLatestForTicketAsync(ticket.Id, cancellationToken) is { IsCompleted: false, Checkpoint: >= IntegrationSagaCheckpoint.IntegrationRefUpdated } saga
            ? $"Ticket run '{ticket.Id}' already has its squash commit on the integration branch (saga at {saga.Checkpoint}); retry it so its stack layer gets published."
            : null;

    private async Task<ControlResult> SaveAsync(TicketRunId ticketRunId, CancellationToken cancellationToken) =>
        await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved
            ? ControlResult.Applied()
            : ControlResult.ConcurrencyConflict($"Ticket run '{ticketRunId}' was changed concurrently; reload it and try again.");
}
