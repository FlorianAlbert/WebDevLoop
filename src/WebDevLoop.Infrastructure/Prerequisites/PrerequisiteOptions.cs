using System.Runtime.InteropServices;
using WebDevLoop.Core.Domain;
using WebDevLoop.Infrastructure.GitHub.Auth;

namespace WebDevLoop.Infrastructure.Prerequisites;

public sealed class PrerequisiteOptions
{
    public const string DefaultGitExecutable = "git";
    public const string DefaultGhExecutable = "gh";
    public const string DefaultPlaywrightCliExecutable = "playwright-cli";

    public required string WorkspaceRoot { get; init; }

    public required GitHubAuthOptions GitHubAuth { get; init; }

    /// <summary>Configured Copilot CLI (<c>WebDevLoop:Copilot:CliPath</c>, see <c>CopilotRuntimeOptions.CliPath</c>); null falls back to the CLI bundled next to the app.</summary>
    public string? CopilotCliPath { get; init; }

    /// <summary>Where the Copilot SDK package copies its CLI when the build bundles it (<c>runtimes/&lt;rid&gt;/native</c>).</summary>
    public string BundledCopilotCliPath { get; init; } = DefaultBundledCopilotCliPath();

    public TestPortRange? TestPortRange { get; init; }

    public GhStackMode GhStackMode { get; init; }

    public string GitExecutable { get; init; } = DefaultGitExecutable;

    public string GhExecutable { get; init; } = DefaultGhExecutable;

    public string PlaywrightCliExecutable { get; init; } = DefaultPlaywrightCliExecutable;

    public TimeSpan ProbeTimeout { get; init; } = TimeSpan.FromSeconds(10);

    private static string DefaultBundledCopilotCliPath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "runtimes",
            RuntimeInformation.RuntimeIdentifier,
            "native",
            OperatingSystem.IsWindows() ? "copilot.exe" : "copilot");
}
