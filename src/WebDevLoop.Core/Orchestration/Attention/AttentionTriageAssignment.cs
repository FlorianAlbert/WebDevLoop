using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Attention;

/// <param name="TicketRunId">Null when the spec run itself needs attention.</param>
public sealed record AttentionTriageAssignment(RunId SpecRunId, TicketRunId? TicketRunId);
