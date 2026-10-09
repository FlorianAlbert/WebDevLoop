using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Events;

/// <summary>Periodic/explicit nudge to recompute a run's frontier so a lost in-process event cannot stall work.</summary>
public sealed record FrontierReconciliationRequested(RunId SpecRunId, DateTimeOffset OccurredAt) : WorkflowEvent(OccurredAt);
