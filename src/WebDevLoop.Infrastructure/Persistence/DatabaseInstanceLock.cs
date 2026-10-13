namespace WebDevLoop.Infrastructure.Persistence;

/// <summary>Another WebDevLoop process already uses the database.</summary>
public sealed class DatabaseInUseException(string message, Exception? innerException = null) : InvalidOperationException(message, innerException);

/// <summary>
/// Holds the database exclusively for this process, so two instances never run migrations, recovery and the workflow against
/// the same data. An open file handle, not a marker file, so the operating system releases it when the process dies.
/// </summary>
public sealed class DatabaseInstanceLock : IDisposable
{
    private const string LockFileSuffix = ".lock";

    private readonly FileStream _handle;

    private DatabaseInstanceLock(FileStream handle) => _handle = handle;

    /// <exception cref="DatabaseInUseException">Another process holds the database.</exception>
    public static DatabaseInstanceLock Acquire(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        try
        {
            return new DatabaseInstanceLock(new FileStream(databasePath + LockFileSuffix, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException exception)
        {
            throw new DatabaseInUseException(
                $"The database {databasePath} is already in use by another WebDevLoop process. Stop that process, or give this one its own data " +
                "directory and database (for example with the environment variables WebDevLoop__DataDirectory and WebDevLoop__DatabasePath).",
                exception);
        }
    }

    public void Dispose() => _handle.Dispose();
}
