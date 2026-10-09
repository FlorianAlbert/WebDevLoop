namespace WebDevLoop.Core.Orchestration.Preparation;

/// <summary>Creates app-owned directories on the server, such as the exploration notes directory of a run.</summary>
public interface IAppDirectoryProvisioner
{
    /// <summary>Idempotent: an existing directory is left as is.</summary>
    void EnsureExists(string absolutePath);
}
