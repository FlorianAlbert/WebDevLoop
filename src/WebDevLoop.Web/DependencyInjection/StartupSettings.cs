using WebDevLoop.Core.Management;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Web.DependencyInjection;

/// <summary>
/// The global settings as resolved once at startup (after migrations and seeding). Process-wide resources are built from
/// them: the git workspace root, the Copilot home, the prerequisite checks, and the PAT fallback of the GitHub adapters.
/// Changing <c>WorkspaceRootDirectory</c>, <c>CopilotBaseDirectory</c> or <c>PatFallbackEnabled</c> therefore takes effect
/// after a restart; per-repository overrides of the workspace root and Copilot home are not supported and ignored.
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
