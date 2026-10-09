namespace WebDevLoop.Core.Domain;

public static class StepStatusRules
{
    public static bool IsActive(this StepStatus status) =>
        status is StepStatus.Pending or StepStatus.Running;

    public static bool CanTransitionTo(this StepStatus from, StepStatus to) => from switch
    {
        StepStatus.Pending => to is StepStatus.Running or StepStatus.Cancelled,
        StepStatus.Running => to is StepStatus.Succeeded
            or StepStatus.Failed
            or StepStatus.TimedOut
            or StepStatus.Cancelled
            or StepStatus.NeedsAttention,
        _ => false,
    };
}
