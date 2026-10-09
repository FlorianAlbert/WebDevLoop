namespace WebDevLoop.Infrastructure.Prerequisites;

public sealed class FileSystemProbe : IFileSystemProbe
{
    public bool FileExists(string path) => File.Exists(path);

    public string? TryEnsureWritableDirectory(string directory)
    {
        string probeFile = Path.Combine(directory, $".webdevloop-write-probe-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(probeFile, string.Empty);
            File.Delete(probeFile);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return exception.Message;
        }
    }
}
