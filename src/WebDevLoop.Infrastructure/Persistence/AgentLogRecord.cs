using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.Persistence;

/// <summary>One persisted agent output line. <see cref="Sequence"/> counts per step from 1 and is never reused after retention deletes old rows.</summary>
public sealed class AgentLogRecord
{
    public required StepRunId StepRunId { get; init; }

    public required int Sequence { get; init; }

    public required DateTimeOffset At { get; init; }

    public required AgentLogKind Kind { get; init; }

    public required string Text { get; init; }
}
