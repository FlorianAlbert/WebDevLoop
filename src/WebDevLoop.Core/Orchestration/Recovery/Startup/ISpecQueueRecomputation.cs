namespace WebDevLoop.Core.Orchestration.Recovery.Startup;

/// <summary>Recovery stage: repairs active-spec slots and reschedules the spec queue of every enabled repository.</summary>
public interface ISpecQueueRecomputation
{
    Task<QueueRecomputationReport> RecomputeAsync(CancellationToken cancellationToken);
}
