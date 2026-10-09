using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Queries;

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
    string? FailureReason);
