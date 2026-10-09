using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Prerequisites;

/// <summary>An explicitly configured CLI path wins and is never replaced by the bundled CLI, mirroring how the runtime pool launches it.</summary>
public sealed class CopilotRuntimeCheck(PrerequisiteOptions options, IFileSystemProbe fileSystem, IProcessProbe processes) : IPrerequisiteCheck
{
    public const string CheckName = "Copilot runtime";

    public string Name => CheckName;

    public async Task<PrerequisiteCheck> RunAsync(CancellationToken cancellationToken)
    {
        bool configured = !string.IsNullOrWhiteSpace(options.CopilotCliPath);
        string cliPath = configured ? options.CopilotCliPath! : options.BundledCopilotCliPath;
        string source = configured ? "configured" : "bundled";

        if (!fileSystem.FileExists(cliPath))
        {
            return CheckResult.Failed(
                Name,
                $"The {source} Copilot CLI was not found at '{cliPath}'.",
                "Set CopilotRuntimeOptions.CliPath to an installed Copilot CLI, or build with -p:CopilotSkipCliDownload=false to bundle it.");
        }

        ProcessProbeResult result = await processes.RunAsync(cliPath, ["--version"], cancellationToken);
        return result.Succeeded
            ? CheckResult.Passed(Name, $"The {source} Copilot CLI at '{cliPath}' runs (version {result.Output}).")
            : CheckResult.Failed(
                Name,
                $"The {source} Copilot CLI at '{cliPath}' cannot run ({result.FailureReason}).",
                "Reinstall the Copilot CLI and make sure the file is executable.");
    }
}
