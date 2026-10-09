using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Queries;

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
    string? FailureReason);
