using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.ReviewLoop;

/// <summary>Applies step and ticket state changes together with their outbox events (saved by the caller's unit of work).</summary>
internal sealed class ReviewLoopJournal(IStepRunRepository stepRuns, IOutbox outbox, IClock clock)
{
    public void Start(StepRun step, TimeSpan timeout)
    {
        step.Start(clock.UtcNow, timeout);
        stepRuns.Add(step);
        outbox.Append(new StepRunStatusChanged(step.SpecRunId, step.TicketRunId, step.Id, step.Status, clock.UtcNow));
    }

    public void Finish(StepRun step, StepStatus status, string? resultJson, string? failure, AttentionReason? attention = null)
    {
        DateTimeOffset now = clock.UtcNow;
        if (status == StepStatus.NeedsAttention)
        {
            step.MarkNeedsAttention(attention!, now, resultJson);
        }
        else
        {
            step.Finish(status, now, resultJson, failure);
        }

        outbox.Append(new StepRunStatusChanged(step.SpecRunId, step.TicketRunId, step.Id, step.Status, now));
    }

    public void Move(TicketRun ticket, TicketRunStatus next)
    {
        DateTimeOffset now = clock.UtcNow;
        TicketRunStatus previous = ticket.Status;
        ticket.TransitionTo(next, now);
        outbox.Append(new TicketRunStatusChanged(ticket.SpecRunId, ticket.Id, previous, next, now));
    }

    public void MarkNeedsAttention(TicketRun ticket, AttentionReason reason)
    {
        DateTimeOffset now = clock.UtcNow;
        TicketRunStatus previous = ticket.Status;
        ticket.MarkNeedsAttention(reason, now);
        outbox.Append(new TicketRunStatusChanged(ticket.SpecRunId, ticket.Id, previous, TicketRunStatus.NeedsAttention, now));
    }
}
