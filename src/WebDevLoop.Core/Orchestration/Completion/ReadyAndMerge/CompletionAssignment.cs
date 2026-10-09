using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;

/// <summary>A spec whose completion work (workflow steps 13–14, or cleanup after an abort) must run.</summary>
public sealed record CompletionAssignment(RunId SpecRunId);
