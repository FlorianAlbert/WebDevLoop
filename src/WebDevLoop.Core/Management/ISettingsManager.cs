namespace WebDevLoop.Core.Management;

public interface ISettingsManager
{
    /// <summary>An all-unset layer until the global profile has been saved for the first time.</summary>
    Task<SettingsProfileData> GetGlobalAsync(CancellationToken cancellationToken);

    Task<CommandResult<SettingsProfileData>> SaveGlobalAsync(SettingsProfileData data, CancellationToken cancellationToken);

    Task<CommandResult<SettingsProfileData>> GetRepositoryAsync(int repositoryId, CancellationToken cancellationToken);

    Task<CommandResult<SettingsProfileData>> SaveRepositoryAsync(int repositoryId, SettingsProfileData data, CancellationToken cancellationToken);

    Task<CommandResult<EffectiveSettingsView>> GetEffectiveAsync(int repositoryId, CancellationToken cancellationToken);
}
