using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.SpecQueue;

/// <summary>
/// Event-bus subscriber that runs the repository queue (<see cref="SpecQueueScheduler"/>) when a spec may start: a spec was
/// queued, or a spec left its active slot (ready/awaiting merge, completed, aborted or needs attention). The scheduler's own claims
/// (<c>WaitingForDependency</c>, <c>Preparing</c>) and other active transitions do not trigger another pass. Periodic
/// recovery recomputes every queue as well, so a missed event only delays a start.
/// </summary>
/// <remarks>
/// After scheduling independent specs, lookup deferrals are deliberately thrown to the event bus's logging boundary,
/// as with frontier reconciliation. The bus logs the failure and continues delivery to other subscribers; it does not
/// roll back saved claims. Normal recovery retries deferred decisions.
/// </remarks>
public sealed class SpecQueueEventHandler(SpecQueueScheduler scheduler)
{
    /// <exception cref="AggregateException">Reports deferred GitHub lookups after the scheduling pass, not a rollback of successful claims.</exception>
    public async Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        SpecScheduleResult result = envelope.Event switch
        {
            SpecRunQueued queued => await scheduler.ScheduleRepositoryAsync(queued.RepositoryId, cancellationToken),
            SpecRunStatusChanged changed when MayStartQueuedSpecs(changed.To) =>
                await scheduler.ScheduleRepositoryAsync(changed.RepositoryId, cancellationToken),
            _ => SpecScheduleResult.Nothing,
        };
        if (result.LookupError is { } error)
        {
            throw error;
        }

        if (result.Faults.Count > 0)
        {
            throw new AggregateException(
                "Some parent blocker checks failed; affected specs were deferred for normal recovery. Independent specs were still scheduled.",
                result.Faults.Select(fault => new GitHubIssuesException(fault.Message)));
        }
    }

    private static bool MayStartQueuedSpecs(SpecRunStatus to) =>
        !to.IsActive() && to is not (SpecRunStatus.Queued or SpecRunStatus.WaitingForDependency);
}
