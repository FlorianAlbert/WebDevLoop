using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.ReviewLoop;

/// <summary>
/// A ticket in <see cref="TicketRunStatus.Reviewing"/> whose review/fix loop must run, or a ticket left in
/// <see cref="TicketRunStatus.FixingReviewFindings"/> without an active step (restart recovery) whose fix must be resumed.
/// </summary>
/// <param name="ResumeFixSessionId">The interrupted fix session to resume; defaults to the original implementer session.</param>
public sealed record ReviewAssignment(RunId SpecRunId, TicketRunId TicketRunId, AgentSessionId? ResumeFixSessionId = null);
