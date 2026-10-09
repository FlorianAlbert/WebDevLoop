using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;

namespace WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;

/// <summary>
/// Workflow step 14 removed <paramref name="Removed"/> implementer worktrees of the spec; dirty, locked, or still used
/// worktrees were retained, each with one entry in <paramref name="Warnings"/>.
/// </summary>
public sealed record SpecWorktreesCleanedUp(
    RunId SpecRunId,
    int RepositoryId,
    int Removed,
    IReadOnlyList<string> Warnings,
    DateTimeOffset OccurredAt) : WorkflowEvent(OccurredAt);
