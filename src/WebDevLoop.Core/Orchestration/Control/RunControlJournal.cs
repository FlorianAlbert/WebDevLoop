using System.Text.Json;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Control;

/// <summary>
/// Applies the state changes of a control command together with their outbox events, and audits the command as a run
/// event plus <see cref="RunControlApplied"/>. Everything is saved by the caller's unit of work.
/// </summary>
public sealed class RunControlJournal(IRunEventRepository runEvents, IOutbox outbox, IClock clock)
{
    private const string CancelledByUser = "Cancelled by the user.";

    public DateTimeOffset Now => clock.UtcNow;

    /// <summary>The run event type a command is audited under, e.g. <c>ControlRetry</c>.</summary>
    public static string RunEventType(ControlAction action) => $"Control{action}";

    public void MoveSpec(SpecRun spec, SpecRunStatus next)
    {
        DateTimeOffset now = clock.UtcNow;
        SpecRunStatus previous = spec.Status;
        spec.TransitionTo(next, now);
        outbox.Append(new SpecRunStatusChanged(spec.Id, spec.RepositoryId, previous, next, now));
    }

    public void MoveTicket(TicketRun ticket, TicketRunStatus next)
    {
        DateTimeOffset now = clock.UtcNow;
        TicketRunStatus previous = ticket.Status;
        ticket.TransitionTo(next, now);
        outbox.Append(new TicketRunStatusChanged(ticket.SpecRunId, ticket.Id, previous, next, now));
    }

    public TicketRunStatus RetryTicket(TicketRun ticket, bool integrationInProgress, TicketRunStatus? resumeAt = null)
    {
        DateTimeOffset now = clock.UtcNow;
        TicketRunStatus previous = ticket.Status;
        TicketRunStatus target = ticket.Retry(now, integrationInProgress, resumeAt);
        outbox.Append(new TicketRunStatusChanged(ticket.SpecRunId, ticket.Id, previous, target, now));
        return target;
    }

    public void CancelStep(StepRun step)
    {
        DateTimeOffset now = clock.UtcNow;
        step.Finish(StepStatus.Cancelled, now, failureReason: CancelledByUser);
        outbox.Append(new StepRunStatusChanged(step.SpecRunId, step.TicketRunId, step.Id, step.Status, now));
    }

    /// <param name="ticketRunId">The ticket the command targeted; null for spec commands.</param>
    /// <param name="status">The status the target moved to.</param>
    /// <param name="alsoAffected">Further tickets the command changed (cascaded skips, tickets of an aborted spec).</param>
    public void Record(ControlAction action, RunId specRunId, TicketRunId? ticketRunId, string status, IEnumerable<TicketRunId>? alsoAffected = null)
    {
        DateTimeOffset now = clock.UtcNow;
        string payload = JsonSerializer.Serialize(new
        {
            action = action.ToString(),
            status,
            tickets = (alsoAffected ?? []).Select(id => id.Value).ToArray(),
        });
        runEvents.Add(RunEvent.Create(specRunId, ticketRunId, RunEventType(action), payload, now));
        outbox.Append(new RunControlApplied(specRunId, ticketRunId, action, now));
    }
}
