using WebDevLoop.Core.Domain;

namespace WebDevLoop.Web.Components.Queue;

public static class SpecRunLanes
{
    public static SpecRunLane For(SpecRunStatus status) => status switch
    {
        _ when status.IsActive() => SpecRunLane.Active,
        SpecRunStatus.Queued or SpecRunStatus.WaitingForDependency => SpecRunLane.Waiting,
        SpecRunStatus.ReadyForReview or SpecRunStatus.AwaitingMerge => SpecRunLane.AwaitingMerge,
        SpecRunStatus.NeedsAttention => SpecRunLane.NeedsAttention,
        _ => SpecRunLane.Completed,
    };

    public static string Title(SpecRunLane lane) => lane switch
    {
        SpecRunLane.Active => "Running",
        SpecRunLane.AwaitingMerge => "Awaiting merge",
        SpecRunLane.NeedsAttention => "Needs attention",
        SpecRunLane.Waiting => "Waiting",
        _ => "Completed",
    };
}
