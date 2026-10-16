using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Orchestration.Recovery.Startup;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Web.Background;

/// <summary>Gates every new agent turn, including retries within an in-flight workflow phase. Aborts remain available.</summary>
public sealed class GatedAgentRunner(IAgentRunner agents, SchedulerStartGate schedulers) : IAgentRunner
{
    public Task<AgentRunResult> StartAsync(AgentRunRequest request, CancellationToken cancellationToken) =>
        schedulers.RunWhenOpenAsync(() => agents.StartAsync(request, cancellationToken), cancellationToken);

    public Task<AgentRunResult> ResumeAsync(AgentRunRequest request, CancellationToken cancellationToken) =>
        schedulers.RunWhenOpenAsync(() => agents.ResumeAsync(request, cancellationToken), cancellationToken);

    public Task AbortAsync(AgentSessionId sessionId, CancellationToken cancellationToken) =>
        agents.AbortAsync(sessionId, cancellationToken);
}
