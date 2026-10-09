using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.SpecQueue;

/// <summary>Effective settings of a repository as orchestration sees them right now (repo override → global → defaults).</summary>
public interface IEffectiveSettingsProvider
{
    Task<EffectiveSettings> GetAsync(int repositoryId, CancellationToken cancellationToken);
}
