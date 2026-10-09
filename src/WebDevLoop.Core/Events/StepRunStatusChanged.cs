using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Events;

public sealed record StepRunStatusChanged(
    RunId SpecRunId,
    TicketRunId? TicketRunId,
    StepRunId StepRunId,
    StepStatus Status,
    DateTimeOffset OccurredAt) : WorkflowEvent(OccurredAt);
