using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Infrastructure.Queries;

/// <summary>
/// Bounded live agent log: the newest entries of the most recently written steps. Logs are a diagnostic view, not a
/// source of truth, so they intentionally do not survive a restart; structured results live on the step run.
/// </summary>
public sealed class InMemoryAgentLogStore(AgentLogStoreOptions options) : IAgentLogSink, IAgentLogReader
{
    private readonly object _gate = new();
    private readonly Dictionary<StepRunId, StepLog> _steps = [];
    private long _writeClock;

    public ValueTask AppendAsync(AgentLogEntry entry, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_steps.TryGetValue(entry.StepRunId, out StepLog? log))
            {
                log = new StepLog();
                _steps[entry.StepRunId] = log;
            }

            log.LastWrite = ++_writeClock;
            log.Entries.Enqueue(new AgentLogView(++log.LastSequence, entry.At, entry.Kind, entry.Text));
            while (log.Entries.Count > options.MaxEntriesPerStep)
            {
                log.Entries.Dequeue();
            }

            EvictLeastRecentlyWrittenSteps();
        }

        return ValueTask.CompletedTask;
    }

    public Task<IReadOnlyList<AgentLogView>> ReadAsync(StepRunId stepRunId, int afterSequence, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<AgentLogView> entries = _steps.TryGetValue(stepRunId, out StepLog? log)
                ? log.Entries.Where(entry => entry.Sequence > afterSequence).ToArray()
                : [];
            return Task.FromResult(entries);
        }
    }

    private void EvictLeastRecentlyWrittenSteps()
    {
        while (_steps.Count > options.MaxSteps)
        {
            StepRunId oldest = _steps.MinBy(pair => pair.Value.LastWrite).Key;
            _steps.Remove(oldest);
        }
    }

    private sealed class StepLog
    {
        public Queue<AgentLogView> Entries { get; } = new();

        public int LastSequence { get; set; }

        public long LastWrite { get; set; }
    }
}
