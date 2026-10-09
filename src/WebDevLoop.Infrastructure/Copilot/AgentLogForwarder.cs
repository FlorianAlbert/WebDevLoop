using System.Threading.Channels;
using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Copilot;

/// <summary>Forwards session output to the log sink in order, off the runtime's event thread.</summary>
internal sealed class AgentLogForwarder : IAsyncDisposable
{
    private readonly StepRunId _stepRunId;
    private readonly IAgentLogSink _sink;
    private readonly IClock _clock;
    private readonly Channel<AgentLogEntry> _entries = Channel.CreateUnbounded<AgentLogEntry>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _pump;

    public AgentLogForwarder(StepRunId stepRunId, IAgentLogSink sink, IClock clock)
    {
        _stepRunId = stepRunId;
        _sink = sink;
        _clock = clock;
        _pump = PumpAsync();
    }

    public void Append(AgentLogKind kind, string text) => _entries.Writer.TryWrite(new AgentLogEntry(_stepRunId, _clock.UtcNow, kind, text));

    public async ValueTask DisposeAsync()
    {
        _entries.Writer.TryComplete();
        await _pump;
    }

    private async Task PumpAsync()
    {
        await foreach (AgentLogEntry entry in _entries.Reader.ReadAllAsync())
        {
            try
            {
                await _sink.AppendAsync(entry, CancellationToken.None);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Live logs are best effort: a failing sink must not fail or stall the agent step.
            }
        }
    }
}
