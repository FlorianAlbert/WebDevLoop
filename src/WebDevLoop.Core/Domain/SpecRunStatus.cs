namespace WebDevLoop.Core.Domain;

public enum SpecRunStatus
{
    Queued,
    WaitingForDependency,
    Preparing,
    Running,
    ParentReviewing,
    Testing,
    ReadyForReview,
    AwaitingMerge,
    Completed,
    NeedsAttention,
    Aborted,

    /// <summary>Eligible tickets are blocked; no active slot is held while scheduling waits to resume this run.</summary>
    WaitingForTickets,
}
