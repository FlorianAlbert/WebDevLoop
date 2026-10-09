using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;

namespace WebDevLoop.Core.Orchestration.Control;

/// <summary>The user applied <paramref name="Action"/> to the spec run, or to one of its tickets when <paramref name="TicketRunId"/> is set.</summary>
public sealed record RunControlApplied(RunId SpecRunId, TicketRunId? TicketRunId, ControlAction Action, DateTimeOffset OccurredAt) : WorkflowEvent(OccurredAt);
