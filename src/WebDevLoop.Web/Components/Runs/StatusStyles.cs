namespace WebDevLoop.Web.Components.Runs;

public static class StatusStyles
{
    /// <summary>Bootstrap badge class for any run, ticket or step status name.</summary>
    public static string BadgeClass(string status) => status switch
    {
        "Succeeded" or "Integrated" or "Completed" or "Clean" => "text-bg-success",
        "Failed" or "TimedOut" or "NeedsAttention" or "Aborted" or "Cancelled" => "text-bg-danger",
        "Ready" or "ReadyForReview" or "AwaitingMerge" => "text-bg-info",
        "Blocked" or "Pending" or "Queued" or "WaitingForDependency" or "Skipped" => "text-bg-secondary",
        _ => "text-bg-primary",
    };
}
