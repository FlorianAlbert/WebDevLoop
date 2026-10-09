using WebDevLoop.Core.Agents;

namespace WebDevLoop.Core.Queries;

/// <summary><see cref="Sequence"/> grows per step starting at 1, so clients resume with "entries after sequence N".</summary>
public sealed record AgentLogView(int Sequence, DateTimeOffset At, AgentLogKind Kind, string Text);
