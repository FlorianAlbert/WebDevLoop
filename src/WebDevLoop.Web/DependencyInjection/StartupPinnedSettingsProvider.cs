using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Web.DependencyInjection;

/// <summary>
/// Pins the startup-scoped directories of the effective settings to the values the process was started with
/// (<see cref="StartupSettings"/>). The git workspace confines every path to that workspace root, so a per-repository override
/// or a runtime change of the global value must never reach the runners that derive worktree and run paths from the settings.
/// </summary>
public sealed class StartupPinnedSettingsProvider(IEffectiveSettingsProvider inner, StartupSettings startup) : IEffectiveSettingsProvider
{
    public async Task<EffectiveSettings> GetAsync(int repositoryId, CancellationToken cancellationToken)
    {
        EffectiveSettings effective = await inner.GetAsync(repositoryId, cancellationToken);
        if (!startup.IsResolved)
        {
            return effective;
        }

        EffectiveSettings pinned = startup.Current;
        return effective with
        {
            WorkspaceRootDirectory = pinned.WorkspaceRootDirectory,
            CopilotBaseDirectory = pinned.CopilotBaseDirectory,
        };
    }
}
