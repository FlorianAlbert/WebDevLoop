using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Prerequisites;

public sealed class PlaywrightCliCheck(PrerequisiteOptions options, IProcessProbe processes) : IPrerequisiteCheck
{
    public const string CheckName = "playwright-cli";

    public string Name => CheckName;

    public async Task<PrerequisiteCheck> RunAsync(CancellationToken cancellationToken)
    {
        ProcessProbeResult result = await processes.RunAsync(options.PlaywrightCliExecutable, ["--version"], cancellationToken);
        return result.Succeeded
            ? CheckResult.Passed(Name, $"playwright-cli is available (version {result.Output}).")
            : CheckResult.Failed(
                Name,
                $"The tester prerequisite '{options.PlaywrightCliExecutable}' is unavailable ({result.FailureReason}).",
                "Install it with: npm install -g @playwright/cli@latest");
    }
}
