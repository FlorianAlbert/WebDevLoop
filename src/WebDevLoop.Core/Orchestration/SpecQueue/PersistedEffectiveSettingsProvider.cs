using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.SpecQueue;

/// <summary>Resolves effective settings from the persisted global profile and the repository's optional override profile.</summary>
public sealed class PersistedEffectiveSettingsProvider(ISettingsProfileRepository profiles, SettingsResolver resolver) : IEffectiveSettingsProvider
{
    /// <exception cref="InvalidOperationException">The global settings profile has not been seeded yet.</exception>
    public async Task<EffectiveSettings> GetAsync(int repositoryId, CancellationToken cancellationToken)
    {
        SettingsProfile global = await profiles.GetGlobalAsync(cancellationToken)
            ?? throw new InvalidOperationException("The global settings profile has not been seeded.");
        SettingsProfile? repository = await profiles.FindForRepositoryAsync(repositoryId, cancellationToken);
        return resolver.Resolve(global, repository);
    }
}
