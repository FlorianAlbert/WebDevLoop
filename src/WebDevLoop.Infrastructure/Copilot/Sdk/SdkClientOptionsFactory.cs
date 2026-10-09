using GitHub.Copilot;
using WebDevLoop.Infrastructure.Copilot.Runtime;

namespace WebDevLoop.Infrastructure.Copilot.Sdk;

internal static class SdkClientOptionsFactory
{
    /// <summary>
    /// A child-process runtime with an app-owned Copilot home and an explicit environment. Sessions authenticate with the
    /// signed-in user's token; stored or logged-in CLI credentials are never used.
    /// </summary>
    public static CopilotClientOptions Create(CopilotRuntimeLaunch launch) => new()
    {
        Connection = RuntimeConnection.ForStdio(launch.CliPath),
        BaseDirectory = launch.BaseDirectory,
        Environment = launch.Environment,
        UseLoggedInUser = false,
    };
}
