namespace WebDevLoop.Infrastructure.Prerequisites;

public interface ILibGit2Probe
{
    /// <summary>Loads the LibGit2Sharp native library and returns its version; throws when it cannot load.</summary>
    string LoadNativeLibrary();
}
