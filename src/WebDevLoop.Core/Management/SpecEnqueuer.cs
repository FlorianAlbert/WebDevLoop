using WebDevLoop.Core.Orchestration.SpecQueue;

namespace WebDevLoop.Core.Management;

public sealed class SpecEnqueuer(SpecQueueService queue) : ISpecEnqueuer
{
    public Task<EnqueueResult> EnqueueAsync(int repositoryId, int specIssueNumber, CancellationToken cancellationToken) =>
        queue.EnqueueAsync(repositoryId, specIssueNumber, cancellationToken);
}
