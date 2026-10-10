using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Queries;

/// <param name="NeedsAttentionFrom">The phase that failed while <paramref name="Status"/> is <c>NeedsAttention</c>.</param>
/// <param name="Attention">Guidance for the user while <paramref name="Status"/> is <c>NeedsAttention</c>; always present then, also for rows stored before reasons were structured.</param>
public sealed record TicketRunView(
    string Id,
    string SpecRunId,
    int IssueNumber,
    string Title,
    TicketRunStatus Status,
    int Attempt,
    int ReviewIteration,
    string BranchName,
    string? WorktreePath,
    string? LastImplementedSha,
    string? IntegratedCommitSha,
    int? PullRequestNumber,
    int? StackPosition,
    IReadOnlyList<string> BlockedByTicketRunIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? FailureReason,
    TicketRunStatus? NeedsAttentionFrom = null,
    AttentionReason? Attention = null);
