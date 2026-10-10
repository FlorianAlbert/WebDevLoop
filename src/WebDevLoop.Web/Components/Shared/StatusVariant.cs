namespace WebDevLoop.Web.Components.Shared;

public enum StatusVariant
{
    Neutral,
    Success,
    Warning,
    Danger,
    Info,
}

public static class StatusVariants
{
    /// <summary>Maps a run, ticket or step status name onto a pill variant.</summary>
    public static StatusVariant For(string status) => status switch
    {
        "Succeeded" or "Integrated" or "Completed" or "Clean" or "Passed" => StatusVariant.Success,
        "NeedsAttention" => StatusVariant.Warning,
        "Failed" or "TimedOut" or "Aborted" or "Cancelled" => StatusVariant.Danger,
        "Ready" or "ReadyForReview" or "AwaitingMerge" or "Running" or "InProgress" => StatusVariant.Info,
        _ => StatusVariant.Neutral,
    };
}
