using System.Collections.Concurrent;
using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Tests.Copilot.Fakes;

internal sealed class RecordingLogSink : IAgentLogSink
{
    public ConcurrentQueue<AgentLogEntry> Entries { get; } = new();

    public ValueTask AppendAsync(AgentLogEntry entry, CancellationToken cancellationToken)
    {
        Entries.Enqueue(entry);
        return ValueTask.CompletedTask;
    }
}
