using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Queries;

/// <param name="PullRequests">The stack's pull requests, bottom to top.</param>
/// <param name="Detail">Why tracking gave up, for <see cref="MergeState.Closed"/>.</param>
public sealed record MergeStatusView(
    string SpecRunId,
    MergeState State,
    SpecRunStatus SpecStatus,
    IReadOnlyList<int> PullRequests,
    string? TopCommitSha,
    DateTimeOffset? ReadyAt,
    DateTimeOffset? CompletedAt,
    string? Detail);
