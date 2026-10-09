using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;

namespace WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;

/// <summary>
/// Merge tracking first saw every PR of the stack merged while trunk does not contain the top layer yet; the spec needs
/// attention if trunk still lacks it after <see cref="ReadyAndMergeOptions.TrunkContainmentTimeout"/>.
/// </summary>
public sealed record SpecStackAwaitingTrunk(RunId SpecRunId, int RepositoryId, PullRequestNumber TopPullRequest, DateTimeOffset OccurredAt)
    : WorkflowEvent(OccurredAt);
