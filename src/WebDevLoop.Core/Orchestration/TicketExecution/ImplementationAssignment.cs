using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.TicketExecution;

/// <summary>A ticket claimed into <see cref="TicketRunStatus.Implementing"/> whose implementer must be started.</summary>
/// <param name="ResumeSessionId">
/// Set by restart recovery: the implementer session that was interrupted. The first attempt resumes it in the preserved
/// worktree; if the session or worktree is gone, the attempt restarts in a fresh session on the same ticket branch.
/// </param>
public sealed record ImplementationAssignment(RunId SpecRunId, TicketRunId TicketRunId, AgentSessionId? ResumeSessionId = null);
