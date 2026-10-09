using WebDevLoop.Core.Orchestration.SpecQueue;

namespace WebDevLoop.Core.Orchestration.Recovery.Startup;

public sealed record RepositoryQueueRecomputation(int RepositoryId, SpecScheduleResult Schedule);

public sealed record QueueRecomputationFault(int RepositoryId, string Error);

/// <param name="Faults">Repositories whose queue could not be recomputed; the other repositories were still scheduled.</param>
public sealed record QueueRecomputationReport(IReadOnlyList<RepositoryQueueRecomputation> Repositories, IReadOnlyList<QueueRecomputationFault> Faults)
{
    public static QueueRecomputationReport Empty { get; } = new([], []);
}
