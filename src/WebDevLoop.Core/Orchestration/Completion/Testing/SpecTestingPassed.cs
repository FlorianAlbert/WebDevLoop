using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;

namespace WebDevLoop.Core.Orchestration.Completion.Testing;

/// <summary>
/// The tester passed the integrated application in test cycle <paramref name="TestCycle"/>; the spec stays in <c>Testing</c>
/// until completion (workflow step 13) verifies the stack and marks it ready. May be appended more than once for the same
/// cycle (e.g. after a restart), so subscribers must be idempotent.
/// </summary>
public sealed record SpecTestingPassed(RunId SpecRunId, int RepositoryId, int TestCycle, DateTimeOffset OccurredAt) : WorkflowEvent(OccurredAt);
