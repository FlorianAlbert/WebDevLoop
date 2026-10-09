namespace WebDevLoop.Core.Queries;

public sealed record StackLayerView(
    int Position,
    string TicketRunId,
    string BranchName,
    string BaseBranch,
    string CommitSha,
    int PullRequestNumber,
    int? StackNumber,
    bool IsDraft,
    string? VerifiedDiffSha);
