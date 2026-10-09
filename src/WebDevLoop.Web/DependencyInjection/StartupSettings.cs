using WebDevLoop.Core.Management;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Web.DependencyInjection;

/// <summary>
/// The global settings as resolved once at startup (after migrations and seeding). Process-wide resources are built from
/// them: the git workspace root, the Copilot home, and the prerequisite checks. Changing <c>WorkspaceRootDirectory</c> or
/// <c>CopilotBaseDirectory</c> therefore takes effect after a restart. The workspace root and the Copilot home are global-only: validation rejects per-repository overrides and
/// <see cref="StartupPinnedSettingsProvider"/> pins them in the effective settings the runners use, so every worktree path
/// stays inside the root the git workspace confines itself to. Existing clones are not moved when the root changes; startup
/// logs a warning for each repository whose clone lies outside the current root.
/// </summary>
public sealed class StartupSettings
{
    private EffectiveSettings? _current;

    public bool IsResolved => _current is not null;

    /// <exception cref="InvalidOperationException">Startup initialization has not resolved the settings yet.</exception>
    public EffectiveSettings Current => _current
        ?? throw new InvalidOperationException("The global settings are resolved during startup initialization, before the workflow starts.");

    public void Resolve(EffectiveSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _current = settings;
    }
}

/// <summary>New repositories are cloned under the workspace root the git workspace was started with (see <see cref="StartupSettings"/>).</summary>
public sealed class StartupWorkspaceRootProvider(StartupSettings settings) : IWorkspaceRootProvider
{
    public Task<string> GetAsync(CancellationToken cancellationToken) => Task.FromResult(settings.Current.WorkspaceRootDirectory);
}
