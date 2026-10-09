using WebDevLoop.Core.Orchestration.Recovery.AgentSteps;

namespace WebDevLoop.Core.Orchestration.Recovery.Startup;

/// <summary>
/// Recovery stage: maintains Copilot runtimes, stops orphaned tester processes, finishes interrupted steps, and relaunches
/// stalled agent work.
/// </summary>
public interface IAgentStepRecovery
{
    Task<AgentStepRecoveryReport> RecoverAsync(CancellationToken cancellationToken);
}
