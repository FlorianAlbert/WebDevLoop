using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Completion.ParentReview;

/// <summary>Applies spec status changes together with their outbox events (saved by the caller's unit of work).</summary>
internal sealed class SpecRunJournal(IOutbox outbox, IClock clock)
{
    public void Move(SpecRun spec, SpecRunStatus next)
    {
        DateTimeOffset now = clock.UtcNow;
        SpecRunStatus previous = spec.Status;
        spec.TransitionTo(next, now);
        outbox.Append(new SpecRunStatusChanged(spec.Id, spec.RepositoryId, previous, next, now));
    }

    public void MarkNeedsAttention(SpecRun spec, string reason)
    {
        DateTimeOffset now = clock.UtcNow;
        SpecRunStatus previous = spec.Status;
        spec.MarkNeedsAttention(reason, now);
        outbox.Append(new SpecRunStatusChanged(spec.Id, spec.RepositoryId, previous, SpecRunStatus.NeedsAttention, now));
    }
}
