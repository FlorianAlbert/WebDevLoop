using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Events;

public sealed record TicketRunStatusChanged(
    RunId SpecRunId,
    TicketRunId TicketRunId,
    TicketRunStatus From,
    TicketRunStatus To,
    DateTimeOffset OccurredAt) : WorkflowEvent(OccurredAt);
