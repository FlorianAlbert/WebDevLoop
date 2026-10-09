using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;

namespace WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;

/// <summary>
/// Workflow step 13 finished: the spec's work is on <paramref name="IntegrationBranch"/> at <paramref name="IntegrationTip"/>,
/// either as <paramref name="PullRequestCount"/> stacked PRs marked ready for review, or (when no PRs exist) with every
/// integrated ticket closed in the tracker.
/// </summary>
public sealed record SpecCompletionReported(
    RunId SpecRunId,
    int RepositoryId,
    BranchName IntegrationBranch,
    CommitSha IntegrationTip,
    int PullRequestCount,
    DateTimeOffset OccurredAt) : WorkflowEvent(OccurredAt);
