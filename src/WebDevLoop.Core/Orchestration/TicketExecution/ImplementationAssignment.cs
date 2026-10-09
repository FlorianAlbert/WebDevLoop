using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.TicketExecution;

/// <summary>A ticket claimed into <see cref="TicketRunStatus.Implementing"/> whose implementer must be started.</summary>
public sealed record ImplementationAssignment(RunId SpecRunId, TicketRunId TicketRunId);
