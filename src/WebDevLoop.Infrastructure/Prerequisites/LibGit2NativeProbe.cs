using LibGit2Sharp;

namespace WebDevLoop.Infrastructure.Prerequisites;

public sealed class LibGit2NativeProbe : ILibGit2Probe
{
    public string LoadNativeLibrary() => GlobalSettings.Version.ToString();
}
