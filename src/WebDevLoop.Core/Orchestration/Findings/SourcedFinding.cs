using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Results;

namespace WebDevLoop.Core.Orchestration.Findings;

/// <summary>A finding together with the agent step that reported it (workflow steps 10 and 12).</summary>
/// <param name="SourceKind"><see cref="StepKind.ParentReview"/> or <see cref="StepKind.Test"/>.</param>
/// <param name="SourceStepRunId">The reviewer or tester step whose structured report contains <paramref name="Finding"/>.</param>
public sealed record SourcedFinding(StepKind SourceKind, StepRunId SourceStepRunId, Finding Finding);
