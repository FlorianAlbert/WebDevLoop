using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.WorkSelection;

/// <summary>Current permission, remaining eligible issues, runnable local tickets and recoverable lookup failures.</summary>
public sealed record CurrentWork(
    string? StopReason,
    string? WaitReason,
    IReadOnlyList<IssueRef> Eligible,
    IReadOnlySet<TicketRunId> Runnable,
    IReadOnlyList<GitHubIssuesException> Errors)
{
    public const string BlockedReason = "Every remaining eligible ticket has an open native blocker. Waiting without an active-spec slot; parent review is deferred. Normal scheduling will retry and resume this run and branch when work becomes runnable.";

    public const string ResumeSlotWaitReason = "Work can resume; waiting for a free active-spec slot without preempting an active spec.";

    /// <summary>Eligible issues whose current native relationships include a confirmed open blocker.</summary>
    public IReadOnlyList<IssueRef> Blocked { get; init; } = [];

    public bool AllEligibleBlocked => Eligible.Count > 0 && Blocked.Count == Eligible.Count;
}
