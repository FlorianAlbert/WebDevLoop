using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;

namespace WebDevLoop.Core.Orchestration.SpecQueue;

/// <summary>A parent spec issue was added to a repository queue; the repository's queue should be scheduled.</summary>
public sealed record SpecRunQueued(RunId SpecRunId, int RepositoryId, DateTimeOffset OccurredAt) : WorkflowEvent(OccurredAt);
