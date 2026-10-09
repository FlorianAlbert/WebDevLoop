using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Queries;

public sealed record StepRunView(
    string Id,
    string SpecRunId,
    string? TicketRunId,
    StepKind Kind,
    AgentRole? AgentRole,
    StepStatus Status,
    int Attempt,
    string? CopilotSessionId,
    string? WorktreePath,
    string? BranchName,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? TimeoutAt,
    string InputPromptHash,
    string? StructuredResultJson,
    string? FailureReason,
    string? Model,
    string? ReasoningEffort);
