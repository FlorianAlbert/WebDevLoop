namespace WebDevLoop.Web.Components.Queue;

internal static partial class SpecPickerLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Loading GitHub spec candidates for repository {RepositoryId} failed.")]
    public static partial void LoadFailed(ILogger logger, Exception exception, int repositoryId);
}
