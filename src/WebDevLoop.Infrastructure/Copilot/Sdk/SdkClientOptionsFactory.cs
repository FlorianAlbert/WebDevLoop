using GitHub.Copilot;
using WebDevLoop.Infrastructure.Copilot.Runtime;

namespace WebDevLoop.Infrastructure.Copilot.Sdk;

internal static class SdkClientOptionsFactory
{
    /// <summary>
    /// A child-process runtime with an app-owned Copilot home and an explicit environment. Installation tokens reach the
    /// runtime only through that environment (<c>COPILOT_GITHUB_TOKEN</c>); stored or logged-in user credentials are never used.
    /// </summary>
    public static CopilotClientOptions Create(CopilotRuntimeLaunch launch) => new()
    {
        Connection = RuntimeConnection.ForStdio(launch.CliPath),
        BaseDirectory = launch.BaseDirectory,
        Environment = launch.Environment,
        UseLoggedInUser = false,
    };
}
