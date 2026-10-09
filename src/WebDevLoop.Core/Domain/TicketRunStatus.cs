namespace WebDevLoop.Core.Domain;

public enum TicketRunStatus
{
    Blocked,
    Ready,
    Implementing,
    Reviewing,
    FixingReviewFindings,
    Integrating,
    Integrated,
    NeedsAttention,
    Skipped,
    Aborted,
}
