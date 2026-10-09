using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Agents;

public sealed record AgentLogEntry(StepRunId StepRunId, DateTimeOffset At, AgentLogKind Kind, string Text);
