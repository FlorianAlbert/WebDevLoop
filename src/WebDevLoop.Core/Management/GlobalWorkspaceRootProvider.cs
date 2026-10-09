using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Management;

/// <summary>Resolves the workspace root from the global settings profile; before it has been seeded the embedded default applies.</summary>
public sealed class GlobalWorkspaceRootProvider(ISettingsProfileRepository profiles, SettingsResolver resolver) : IWorkspaceRootProvider
{
    public async Task<string> GetAsync(CancellationToken cancellationToken) =>
        resolver.Resolve(await profiles.GetGlobalAsync(cancellationToken) ?? SettingsProfile.ForGlobal()).WorkspaceRootDirectory;
}
