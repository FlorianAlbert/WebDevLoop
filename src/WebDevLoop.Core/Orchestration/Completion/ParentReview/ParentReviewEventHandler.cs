using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;

namespace WebDevLoop.Core.Orchestration.Completion.ParentReview;

/// <summary>
/// Event-bus subscriber for workflow steps 9–10: a ticket becoming integrated, skipped, or aborted, or a spec (re)entering
/// <c>Running</c>, checks whether to defer, finish or review (<see cref="WorkCompletionDecider"/>); a spec
/// entering <c>ParentReviewing</c> has its review launched in the background (<see cref="IParentReviewLauncher"/>).
/// </summary>
public sealed class ParentReviewEventHandler(WorkCompletionDecider completion, IParentReviewLauncher launcher)
{
    public async Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        switch (envelope.Event)
        {
            case FrontierReconciliationRequested requested:
                await DecideCompletionAsync(requested.SpecRunId, cancellationToken);
                break;
            case TicketRunStatusChanged { To: TicketRunStatus.Integrated or TicketRunStatus.Skipped or TicketRunStatus.Aborted } changed:
                await DecideCompletionAsync(changed.SpecRunId, cancellationToken);
                break;
            case SpecRunStatusChanged { To: SpecRunStatus.Running } running:
                await DecideCompletionAsync(running.SpecRunId, cancellationToken);
                break;
            case SpecRunStatusChanged { To: SpecRunStatus.ParentReviewing } reviewing:
                launcher.Launch(new ParentReviewAssignment(reviewing.SpecRunId));
                break;
        }
    }

    private async Task DecideCompletionAsync(RunId specRunId, CancellationToken cancellationToken)
    {
        WorkCompletionResult result = await completion.DecideCompletionAsync(specRunId, cancellationToken);
        if (result.LookupError is { } error)
        {
            throw error;
        }
    }
}
