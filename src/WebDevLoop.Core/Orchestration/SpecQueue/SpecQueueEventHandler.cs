using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;

namespace WebDevLoop.Core.Orchestration.SpecQueue;

/// <summary>
/// Event-bus subscriber that runs the repository queue (<see cref="SpecQueueScheduler"/>) when a spec may start: a spec was
/// queued, or a spec left its active slot or reached a state a dependent waits for (ready/awaiting merge for
/// <c>StackOnTop</c>, completed for <c>WaitForMerge</c>, aborted or needs attention). The scheduler's own claims
/// (<c>WaitingForDependency</c>, <c>Preparing</c>) and other active transitions do not trigger another pass. Periodic
/// recovery recomputes every queue as well, so a missed event only delays a start.
/// </summary>
public sealed class SpecQueueEventHandler(SpecQueueScheduler scheduler)
{
    public async Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        switch (envelope.Event)
        {
            case SpecRunQueued queued:
                await scheduler.ScheduleRepositoryAsync(queued.RepositoryId, cancellationToken);
                break;
            case SpecRunStatusChanged changed when MayStartQueuedSpecs(changed.To):
                await scheduler.ScheduleRepositoryAsync(changed.RepositoryId, cancellationToken);
                break;
        }
    }

    private static bool MayStartQueuedSpecs(SpecRunStatus to) =>
        !to.IsActive() && to is not (SpecRunStatus.Queued or SpecRunStatus.WaitingForDependency);
}
