namespace WebDevLoop.Core.Orchestration.Recovery.Startup;

/// <summary>Recovery stage: reschedules the spec queue of every enabled repository.</summary>
public interface ISpecQueueRecomputation
{
    Task<QueueRecomputationReport> RecomputeAsync(CancellationToken cancellationToken);
}
