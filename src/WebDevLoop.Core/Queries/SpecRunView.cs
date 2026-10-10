using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Queries;

/// <param name="NeedsAttentionFrom">The phase that failed while <paramref name="Status"/> is <c>NeedsAttention</c>.</param>
/// <param name="Attention">Guidance for the user while <paramref name="Status"/> is <c>NeedsAttention</c>; always present then, also for rows stored before reasons were structured.</param>
public sealed record SpecRunView(
    string Id,
    int RepositoryId,
    int ParentIssueNumber,
    string Title,
    SpecRunStatus Status,
    int QueuePosition,
    string? BaseBranch,
    string IntegrationBranch,
    string? IntegrationBaseSha,
    string? IntegrationTipSha,
    SpecDependencyMode? DependencyModeUsed,
    int ReviewCycle,
    int TestCycle,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? ReadyAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason,
    SpecRunStatus? NeedsAttentionFrom = null,
    AttentionReason? Attention = null);
