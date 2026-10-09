using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Settings;
using WebDevLoop.Core.Tests.Settings;

namespace WebDevLoop.Core.Tests.Orchestration.TicketExecution;

/// <summary>Effective settings per repository: embedded defaults under a test workspace root, adjustable per repository.</summary>
internal sealed class FixedSettingsProvider(string workspaceRoot) : IEffectiveSettingsProvider
{
    private readonly Dictionary<int, EffectiveSettings> _byRepository = [];

    public EffectiveSettings Defaults { get; set; } = TestSettings.EmbeddedDefaults() with { WorkspaceRootDirectory = workspaceRoot };

    public void Configure(int repositoryId, Func<EffectiveSettings, EffectiveSettings> change) =>
        _byRepository[repositoryId] = change(For(repositoryId));

    public EffectiveSettings For(int repositoryId) => _byRepository.GetValueOrDefault(repositoryId, Defaults);

    public Task<EffectiveSettings> GetAsync(int repositoryId, CancellationToken cancellationToken) => Task.FromResult(For(repositoryId));
}
