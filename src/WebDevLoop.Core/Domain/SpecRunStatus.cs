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
}
