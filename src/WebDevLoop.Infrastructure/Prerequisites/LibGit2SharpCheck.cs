using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Prerequisites;

public sealed class LibGit2SharpCheck(ILibGit2Probe libGit2) : IPrerequisiteCheck
{
    public const string CheckName = "LibGit2Sharp";

    public string Name => CheckName;

    public Task<PrerequisiteCheck> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            return Task.FromResult(CheckResult.Passed(Name, $"LibGit2Sharp native library loaded (version {libGit2.LoadNativeLibrary()})."));
        }
        catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException or TypeInitializationException)
        {
            return Task.FromResult(CheckResult.Failed(
                Name,
                $"The LibGit2Sharp native library cannot be loaded: {exception.Message}",
                "Run on a supported platform/runtime identifier and keep the runtimes folder next to the app."));
        }
    }
}
