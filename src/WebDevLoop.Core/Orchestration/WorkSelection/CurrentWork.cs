using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.WorkSelection;

/// <summary>Current permission, remaining eligible issues, runnable local tickets and recoverable lookup failures.</summary>
public sealed record CurrentWork(
    string? StopReason,
    string? WaitReason,
    IReadOnlyList<IssueRef> Eligible,
    IReadOnlySet<TicketRunId> Runnable,
    IReadOnlyList<GitHubIssuesException> Errors);
