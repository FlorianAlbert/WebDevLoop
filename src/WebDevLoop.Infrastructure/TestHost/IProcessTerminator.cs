namespace WebDevLoop.Infrastructure.TestHost;

internal interface IProcessTerminator
{
    /// <summary>Kills the process and its descendants.</summary>
    /// <returns>False when the process no longer exists or cannot be killed.</returns>
    bool Kill(int processId);
}
