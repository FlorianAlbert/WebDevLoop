using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

/// <summary>Persisted settings layers (global row and nullable per-repository overrides) feeding settings resolution.</summary>
public interface ISettingsProfileRepository
{
    /// <summary>Null until the global profile has been seeded on first run.</summary>
    Task<SettingsProfile?> GetGlobalAsync(CancellationToken cancellationToken);

    Task<SettingsProfile?> FindForRepositoryAsync(int repositoryId, CancellationToken cancellationToken);

    void Add(SettingsProfile profile);
}
