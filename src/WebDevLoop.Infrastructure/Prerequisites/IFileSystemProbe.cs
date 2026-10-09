namespace WebDevLoop.Infrastructure.Prerequisites;

public interface IFileSystemProbe
{
    bool FileExists(string path);

    /// <summary>Creates the directory if needed and proves it is writable; returns the problem, or null when it works.</summary>
    string? TryEnsureWritableDirectory(string directory);
}
