using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Completion.Testing;

/// <summary>A spec in <see cref="SpecRunStatus.Testing"/> whose tester run (workflow steps 11–12) must run.</summary>
public sealed record TestingAssignment(RunId SpecRunId);
