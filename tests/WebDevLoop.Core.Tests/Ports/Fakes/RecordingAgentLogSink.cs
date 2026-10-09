using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Ports.Fakes;

public sealed class RecordingAgentLogSink : IAgentLogSink
{
    public List<AgentLogEntry> Entries { get; } = [];

    public ValueTask AppendAsync(AgentLogEntry entry, CancellationToken cancellationToken)
    {
        Entries.Add(entry);
        return ValueTask.CompletedTask;
    }
}
