using WebDevLoop.Core.Management;

namespace WebDevLoop.Web.Background;

/// <summary>Re-fetches the Model Catalog and re-validates after successful settings saves from either the UI or API.</summary>
public sealed class ValidatingSettingsManager(SettingsManager settings, ModelCatalogReadiness models) : ISettingsManager
{
    public Task<SettingsProfileData> GetGlobalAsync(CancellationToken cancellationToken) =>
        settings.GetGlobalAsync(cancellationToken);

    public Task<CommandResult<SettingsProfileData>> GetRepositoryAsync(int repositoryId, CancellationToken cancellationToken) =>
        settings.GetRepositoryAsync(repositoryId, cancellationToken);

    public Task<CommandResult<EffectiveSettingsView>> GetEffectiveAsync(int repositoryId, CancellationToken cancellationToken) =>
        settings.GetEffectiveAsync(repositoryId, cancellationToken);

    public async Task<CommandResult<SettingsProfileData>> SaveGlobalAsync(SettingsProfileData data, CancellationToken cancellationToken) =>
        await ValidateSavedAsync(await settings.SaveGlobalAsync(data, cancellationToken), cancellationToken);

    public async Task<CommandResult<SettingsProfileData>> SaveRepositoryAsync(int repositoryId, SettingsProfileData data, CancellationToken cancellationToken) =>
        await ValidateSavedAsync(await settings.SaveRepositoryAsync(repositoryId, data, cancellationToken), cancellationToken);

    private async Task<CommandResult<SettingsProfileData>> ValidateSavedAsync(CommandResult<SettingsProfileData> result, CancellationToken cancellationToken)
    {
        if (result.IsSuccess)
        {
            await models.RefreshAsync(cancellationToken);
        }

        return result;
    }
}
