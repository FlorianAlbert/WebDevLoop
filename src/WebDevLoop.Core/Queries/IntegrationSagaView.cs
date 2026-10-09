using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Queries;

public sealed record IntegrationSagaView(
    string TicketRunId,
    IntegrationSagaCheckpoint Checkpoint,
    string StackBranch,
    int? PullRequestNumber,
    string? LastError,
    DateTimeOffset UpdatedAt);
