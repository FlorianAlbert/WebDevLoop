using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Prerequisites;

/// <summary>
/// The app itself uses LibGit2Sharp in-process, but agents run local <c>git</c> (status, diff, commit) in their shells.
/// </summary>
public sealed class GitCliCheck(PrerequisiteOptions options, IProcessProbe processes) : IPrerequisiteCheck
{
    public const string CheckName = "git CLI";

    public string Name => CheckName;

    public async Task<PrerequisiteCheck> RunAsync(CancellationToken cancellationToken)
    {
        ProcessProbeResult result = await processes.RunAsync(options.GitExecutable, ["--version"], cancellationToken);
        return result.Succeeded
            ? CheckResult.Passed(Name, result.Output)
            : CheckResult.Failed(
                Name,
                $"'{options.GitExecutable}' is unavailable ({result.FailureReason}); agents need it for local commits and diffs.",
                "Install git and make sure it is on PATH for the app user.");
    }
}
