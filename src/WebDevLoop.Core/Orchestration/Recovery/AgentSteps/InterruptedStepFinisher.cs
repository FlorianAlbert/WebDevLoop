using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Recovery.AgentSteps;

/// <summary>
/// Finishes running steps that no live session owns any more: steps started by a previous process (their session died
/// with it), tester steps whose lease was stopped, and other steps still running longer than the stall grace period past
/// their timeout (runners enforce the timeout themselves, so the runner is gone; its session is aborted to be sure). The
/// reason is recorded on the step and in its log. A step whose
/// owner has now been interrupted more often in a row than <c>MaxRetries</c> allows moves the owner to
/// <c>NeedsAttention</c> instead of being retried again, so a crash loop cannot restart the same work forever.
/// </summary>
public sealed class InterruptedStepFinisher(
    ISpecRunRepository specRuns,
    ITicketRunRepository ticketRuns,
    IStepRunRepository stepRuns,
    IEffectiveSettingsProvider settings,
    IAgentRunner agents,
    IAgentLogSink logs,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock,
    ProcessBoot boot,
    AgentStepRecoveryOptions options)
{
    public async Task<StepInterruptionResult> FinishAsync(IReadOnlyList<StoppedTestLease> stoppedLeases, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stoppedLeases);
        Dictionary<RunId, StoppedTestLease> leasesBySpec = stoppedLeases.ToDictionary(lease => lease.SpecRunId);
        StepRunId[] orphaned = (await stepRuns.ListActiveAsync(cancellationToken))
            .Where(step => IsOrphaned(step, leasesBySpec))
            .Select(step => step.Id)
            .ToArray();

        var result = new ResultBuilder();
        foreach (StepRunId id in orphaned)
        {
            // Reloaded per step: a lost save clears the unit of work.
            if (await stepRuns.GetAsync(id, cancellationToken) is { } step && IsOrphaned(step, leasesBySpec))
            {
                await FinishAsync(step, leasesBySpec.GetValueOrDefault(step.SpecRunId), result, cancellationToken);
            }
        }

        return result.Build();
    }

    private bool IsOrphaned(StepRun step, Dictionary<RunId, StoppedTestLease> stoppedLeases) =>
        step.Status == StepStatus.Running
        && (IsFromPreviousProcess(step) || (step.Kind == StepKind.Test && stoppedLeases.ContainsKey(step.SpecRunId)) || IsOverdue(step));

    private bool IsFromPreviousProcess(StepRun step) => step.StartedAt is { } startedAt && boot.IsBeforeBoot(startedAt);

    /// <summary>Test steps are bounded by their lease's time-to-live instead.</summary>
    private bool IsOverdue(StepRun step) =>
        step.Kind != StepKind.Test && step.TimeoutAt is { } timeoutAt && timeoutAt + options.StallGracePeriod <= clock.UtcNow;

    private async Task FinishAsync(StepRun step, StoppedTestLease? lease, ResultBuilder result, CancellationToken cancellationToken)
    {
        (StepStatus status, string reason) = Outcome(step, lease);
        if (!IsFromPreviousProcess(step) && step.CopilotSessionId is { } session)
        {
            await AbortAsync(new AgentSessionId(session), cancellationToken);
        }

        DateTimeOffset now = clock.UtcNow;
        step.Finish(status, now, failureReason: reason);
        outbox.Append(new StepRunStatusChanged(step.SpecRunId, step.TicketRunId, step.Id, step.Status, now));
        Owner? escalated = await EscalateIfRetriesExhaustedAsync(step, cancellationToken);
        if (await unitOfWork.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
        {
            result.Conflicts++;
            return;
        }

        await logs.AppendAsync(new AgentLogEntry(step.Id, now, AgentLogKind.Error, reason), cancellationToken);
        result.Interrupted.Add(new InterruptedStep(step.Id, step.Kind, step.SpecRunId, step.TicketRunId));
        switch (escalated)
        {
            case { TicketRunId: { } ticketRunId }:
                result.Tickets.Add(ticketRunId);
                break;
            case { SpecRunId: { } specRunId }:
                result.Specs.Add(specRunId);
                break;
        }
    }

    private (StepStatus Status, string Reason) Outcome(StepRun step, StoppedTestLease? lease) => (step.Kind, lease) switch
    {
        (StepKind.Test, { Expired: true }) => (StepStatus.TimedOut, StepInterruption.LeaseExpired(lease.Port, lease.KilledProcesses)),
        (StepKind.Test, { }) => (StepStatus.Failed, StepInterruption.LeaseReleased(lease.Port, lease.KilledProcesses)),
        _ when !IsFromPreviousProcess(step) && step.TimeoutAt is { } timeoutAt => (StepStatus.TimedOut, StepInterruption.Overdue(timeoutAt)),
        _ => (StepStatus.Failed, StepInterruption.Restarted),
    };

    /// <summary>Best effort: a session the runner no longer tracks is unknown to the agent runner, which ignores it.</summary>
    private async Task AbortAsync(AgentSessionId session, CancellationToken cancellationToken)
    {
        try
        {
            await agents.AbortAsync(session, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Finishing the step must not depend on the session still being reachable.
        }
    }

    /// <returns>The owner moved to <c>NeedsAttention</c>, or null when its work may be retried.</returns>
    private async Task<Owner?> EscalateIfRetriesExhaustedAsync(StepRun step, CancellationToken cancellationToken)
    {
        if (await specRuns.GetAsync(step.SpecRunId, cancellationToken) is not { } spec)
        {
            return null;
        }

        IReadOnlyList<StepRun> history = step.TicketRunId is { } ticketRunId
            ? await stepRuns.ListByTicketRunAsync(ticketRunId, cancellationToken)
            : (await stepRuns.ListBySpecRunAsync(spec.Id, cancellationToken)).Where(candidate => candidate.TicketRunId is null).ToArray();
        int interruptions = ConsecutiveInterruptions(history, step);
        int maxRetries = (await settings.GetAsync(spec.RepositoryId, cancellationToken)).MaxRetries;
        if (interruptions <= maxRetries)
        {
            return null;
        }

        string reason = $"The {step.Kind} step was interrupted {interruptions} times in a row (restarts or lost runners); at most {maxRetries} retries are allowed.";
        return step.TicketRunId is { } ticketId
            ? await EscalateTicketAsync(ticketId, step.Kind, reason, cancellationToken)
            : EscalateSpec(spec, step.Kind, reason);
    }

    /// <summary>Interrupted steps of the same kind and role (review axis) in a row, newest first, including <paramref name="step"/>.</summary>
    private static int ConsecutiveInterruptions(IEnumerable<StepRun> history, StepRun step) =>
        history.Where(candidate => candidate.Kind == step.Kind && candidate.AgentRole == step.AgentRole)
            .OrderByDescending(candidate => candidate.StartedAt)
            .ThenByDescending(candidate => candidate.Attempt)
            .TakeWhile(StepInterruption.IsInterruption)
            .Count();

    private async Task<Owner?> EscalateTicketAsync(TicketRunId ticketRunId, StepKind kind, string reason, CancellationToken cancellationToken)
    {
        if (await ticketRuns.GetAsync(ticketRunId, cancellationToken) is not { } ticket || ticket.Status != WorkingStatus.OfTicketFor(kind))
        {
            return null;
        }

        DateTimeOffset now = clock.UtcNow;
        TicketRunStatus previous = ticket.Status;
        ticket.MarkNeedsAttention(reason, now);
        outbox.Append(new TicketRunStatusChanged(ticket.SpecRunId, ticket.Id, previous, TicketRunStatus.NeedsAttention, now));
        return new Owner(null, ticket.Id);
    }

    private Owner? EscalateSpec(SpecRun spec, StepKind kind, string reason)
    {
        if (spec.Status != WorkingStatus.OfSpecFor(kind))
        {
            return null;
        }

        DateTimeOffset now = clock.UtcNow;
        SpecRunStatus previous = spec.Status;
        spec.MarkNeedsAttention(reason, now);
        outbox.Append(new SpecRunStatusChanged(spec.Id, spec.RepositoryId, previous, SpecRunStatus.NeedsAttention, now));
        return new Owner(spec.Id, null);
    }

    private sealed record Owner(RunId? SpecRunId, TicketRunId? TicketRunId);

    private sealed class ResultBuilder
    {
        public List<InterruptedStep> Interrupted { get; } = [];

        public List<TicketRunId> Tickets { get; } = [];

        public List<RunId> Specs { get; } = [];

        public int Conflicts { get; set; }

        public StepInterruptionResult Build() => new(Interrupted, Tickets, Specs, Conflicts);
    }
}
