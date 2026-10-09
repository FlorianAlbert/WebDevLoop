using WebDevLoop.Core.Agents;

namespace WebDevLoop.Core.Ports;

/// <summary>Receives live agent output for step logs and the UI.</summary>
public interface IAgentLogSink
{
    ValueTask AppendAsync(AgentLogEntry entry, CancellationToken cancellationToken);
}
