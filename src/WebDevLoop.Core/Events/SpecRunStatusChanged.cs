using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Events;

public sealed record SpecRunStatusChanged(
    RunId SpecRunId,
    int RepositoryId,
    SpecRunStatus From,
    SpecRunStatus To,
    DateTimeOffset OccurredAt) : WorkflowEvent(OccurredAt);
