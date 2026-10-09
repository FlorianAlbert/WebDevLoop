using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.ReviewLoop;

/// <summary>A ticket in <see cref="TicketRunStatus.Reviewing"/> whose review/fix loop must run.</summary>
public sealed record ReviewAssignment(RunId SpecRunId, TicketRunId TicketRunId);
