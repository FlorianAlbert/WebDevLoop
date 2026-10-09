using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Recovery.AgentSteps;

public enum RecoveredWorkKind
{
    Implementation,
    ReviewLoop,
    Integration,
    ParentReview,
    Testing,
}

/// <summary>Work recovery handed back to its launcher.</summary>
/// <param name="ResumeSessionId">The interrupted Copilot session the relaunched runner resumes first, if any.</param>
public sealed record RecoveredWork(RecoveredWorkKind Kind, RunId SpecRunId, TicketRunId? TicketRunId = null, AgentSessionId? ResumeSessionId = null);
