namespace WebDevLoop.Core.Orchestration.Preparation;

public sealed class FileSystemAppDirectoryProvisioner : IAppDirectoryProvisioner
{
    public void EnsureExists(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        if (!Path.IsPathFullyQualified(absolutePath))
        {
            throw new ArgumentException($"'{absolutePath}' must be an absolute path.", nameof(absolutePath));
        }

        Directory.CreateDirectory(absolutePath);
    }
}
