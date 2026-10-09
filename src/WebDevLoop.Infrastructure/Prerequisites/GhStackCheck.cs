using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Prerequisites;

/// <summary><c>gh stack</c> is only the fallback when the stack REST API is unavailable, so it blocks only in <see cref="GhStackMode.FallbackRequired"/>.</summary>
public sealed class GhStackCheck(PrerequisiteOptions options, IProcessProbe processes) : IPrerequisiteCheck
{
    public const string CheckName = "gh stack";

    public string Name => CheckName;

    public async Task<PrerequisiteCheck> RunAsync(CancellationToken cancellationToken)
    {
        ProcessProbeResult gh = await processes.RunAsync(options.GhExecutable, ["--version"], cancellationToken);
        if (!gh.Succeeded)
        {
            return Unavailable($"'{options.GhExecutable}' is unavailable ({gh.FailureReason}).", "Install the GitHub CLI (gh).");
        }

        ProcessProbeResult stack = await processes.RunAsync(options.GhExecutable, ["stack", "--help"], cancellationToken);
        return stack.Succeeded
            ? CheckResult.Passed(Name, "The GitHub CLI and its 'gh stack' extension are available.")
            : Unavailable("The 'gh stack' extension is not installed.", "Run: gh extension install github/gh-stack");
    }

    private PrerequisiteCheck Unavailable(string message, string remediation) => options.GhStackMode == GhStackMode.FallbackRequired
        ? CheckResult.Failed(Name, $"{message} The 'gh stack' fallback is required.", remediation)
        : CheckResult.Warning(Name, $"{message} It is only needed if the stack REST API is unavailable.", remediation);
}
