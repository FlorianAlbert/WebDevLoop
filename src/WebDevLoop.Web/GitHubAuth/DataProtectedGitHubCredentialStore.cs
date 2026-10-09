using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using WebDevLoop.Infrastructure.GitHub.Auth;

namespace WebDevLoop.Web.GitHubAuth;

/// <summary>
/// Keeps the signed-in user's GitHub tokens in one file, encrypted with ASP.NET Core data protection (keys under the data
/// directory). On Unix the file and the key directory are readable by the current user only. Losing the keys only loses
/// the sign-in: the file can no longer be read and the user signs in again.
/// </summary>
public sealed partial class DataProtectedGitHubCredentialStore : IGitHubCredentialStore
{
    private const string Purpose = "WebDevLoop.GitHubUserCredentials.v1";

    private readonly IDataProtector _protector;
    private readonly string _path;
    private readonly ILogger<DataProtectedGitHubCredentialStore> _logger;
    private readonly Lock _gate = new();

    public DataProtectedGitHubCredentialStore(
        IDataProtectionProvider protection,
        string path,
        string keysDirectory,
        ILogger<DataProtectedGitHubCredentialStore> logger)
    {
        _protector = protection.CreateProtector(Purpose);
        _path = path;
        _logger = logger;
        CreatePrivateDirectory(keysDirectory);
    }

    public GitHubUserCredentials? Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            try
            {
                byte[] json = _protector.Unprotect(File.ReadAllBytes(_path));
                return JsonSerializer.Deserialize<GitHubUserCredentials>(json);
            }
            catch (Exception exception) when (exception is CryptographicException or JsonException or IOException or UnauthorizedAccessException)
            {
                LogUnreadable(_logger, _path, exception);
                return null;
            }
        }
    }

    public void Save(GitHubUserCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        byte[] protectedJson = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(credentials));
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temporary = _path + ".tmp";
            using (FileStream file = OperatingSystem.IsWindows()
                ? new FileStream(temporary, FileMode.Create, FileAccess.Write)
                : new FileStream(temporary, new FileStreamOptions
                {
                    Mode = FileMode.Create,
                    Access = FileAccess.Write,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
                }))
            {
                file.Write(protectedJson);
            }

            File.Move(temporary, _path, overwrite: true);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            File.Delete(_path);
        }
    }

    private void CreatePrivateDirectory(string directory)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(directory);
            }
            else
            {
                Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The data directory is unusable; the database check reports it and saving the sign-in fails with the cause.
            LogKeysDirectoryUnavailable(_logger, directory, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The stored GitHub sign-in at {Path} cannot be read (e.g. the data-protection keys changed); sign in with GitHub again.")]
    private static partial void LogUnreadable(ILogger logger, string path, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The data-protection key directory {Directory} cannot be created.")]
    private static partial void LogKeysDirectoryUnavailable(ILogger logger, string directory, Exception exception);
}
