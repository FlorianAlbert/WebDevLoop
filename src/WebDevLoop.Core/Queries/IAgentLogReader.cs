using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Queries;

public interface IAgentLogReader
{
    Task<IReadOnlyList<AgentLogView>> ReadAsync(StepRunId stepRunId, int afterSequence, CancellationToken cancellationToken);
}
