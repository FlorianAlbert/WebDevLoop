namespace WebDevLoop.Core.Management;

/// <summary>The server-side workspace root under which repositories are cloned (global setting, embedded default otherwise).</summary>
public interface IWorkspaceRootProvider
{
    Task<string> GetAsync(CancellationToken cancellationToken);
}
