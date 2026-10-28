using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Completion;

/// <summary>A completion outcome and any lookup failure to report at the event/logging boundary.</summary>
public sealed record WorkCompletionResult(WorkCompletionOutcome Outcome, GitHubIssuesException? LookupError = null);
