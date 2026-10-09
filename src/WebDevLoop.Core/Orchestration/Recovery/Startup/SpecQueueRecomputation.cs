using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Recovery.Startup;

/// <summary>
/// Recomputes every enabled repository's spec queue from the database with a normal scheduling pass (dependency waits,
/// slot claims). Specs leave their active slot whenever they leave an active phase (e.g. parked in <c>NeedsAttention</c>
/// by recovery), so the pass sees the free slots; a spec re-activated outside the queue takes its slot from
/// <see cref="SpecQueueScheduler.FindFreeSlotAsync"/>. Specs a pass activates move to <c>Preparing</c> with their status
/// event in the outbox, so preparation starts once the outbox is dispatched. A repository that fails is reported and
/// does not stop the others; a lost claim race is left to the queue worker's next pass.
/// </summary>
public sealed class SpecQueueRecomputation(IRepositoryRecordRepository repositories, SpecQueueScheduler scheduler) : ISpecQueueRecomputation
{
    public async Task<QueueRecomputationReport> RecomputeAsync(CancellationToken cancellationToken)
    {
        var recomputed = new List<RepositoryQueueRecomputation>();
        var faults = new List<QueueRecomputationFault>();
        foreach (RepositoryRecord repository in (await repositories.ListAsync(cancellationToken)).Where(repository => repository.IsEnabled).OrderBy(repository => repository.Id))
        {
            try
            {
                SpecScheduleResult schedule = await scheduler.ScheduleRepositoryAsync(repository.Id, cancellationToken);
                recomputed.Add(new RepositoryQueueRecomputation(repository.Id, schedule));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                faults.Add(new QueueRecomputationFault(repository.Id, exception.Message));
            }
        }

        return new QueueRecomputationReport(recomputed, faults);
    }
}
