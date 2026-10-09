using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;

namespace WebDevLoop.Core.Orchestration.Completion.ParentReview;

/// <summary>
/// Event-bus subscriber for workflow steps 9–10: a ticket becoming integrated, skipped, or aborted, or a spec (re)entering
/// <c>Running</c> with every ticket already done, starts the parent-spec review (<see cref="ParentReviewStarter"/>); a spec
/// entering <c>ParentReviewing</c> has its review launched in the background (<see cref="IParentReviewLauncher"/>).
/// </summary>
public sealed class ParentReviewEventHandler(ParentReviewStarter starter, IParentReviewLauncher launcher)
{
    public async Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        switch (envelope.Event)
        {
            case TicketRunStatusChanged { To: TicketRunStatus.Integrated or TicketRunStatus.Skipped or TicketRunStatus.Aborted } changed:
                await starter.StartIfTicketsCompleteAsync(changed.SpecRunId, cancellationToken);
                break;
            case SpecRunStatusChanged { To: SpecRunStatus.Running } running:
                await starter.StartIfTicketsCompleteAsync(running.SpecRunId, cancellationToken);
                break;
            case SpecRunStatusChanged { To: SpecRunStatus.ParentReviewing } reviewing:
                launcher.Launch(new ParentReviewAssignment(reviewing.SpecRunId));
                break;
        }
    }
}
