namespace WebDevLoop.Web.Components.Queue;

/// <summary>The dashboard/queue grouping of spec runs, in display order.</summary>
public enum SpecRunLane
{
    Active,
    AwaitingMerge,
    NeedsAttention,
    Waiting,
    Completed,
}
