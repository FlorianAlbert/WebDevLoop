using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Events;

/// <summary>An integration saga was started, advanced to <paramref name="Checkpoint"/>, or rewound to <see cref="IntegrationSagaCheckpoint.Started"/> to squash again.</summary>
public sealed record SagaCheckpointAdvanced(
    RunId SpecRunId,
    TicketRunId TicketRunId,
    IntegrationSagaCheckpoint Checkpoint,
    DateTimeOffset OccurredAt) : WorkflowEvent(OccurredAt);
