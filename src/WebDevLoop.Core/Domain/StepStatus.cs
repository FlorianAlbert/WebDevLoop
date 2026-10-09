namespace WebDevLoop.Core.Domain;

public enum StepStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    TimedOut,
    Cancelled,
    NeedsAttention,
}
