using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.SpecQueue;

public sealed record EnqueueResult(EnqueueOutcome Outcome, RunId? SpecRunId = null);
