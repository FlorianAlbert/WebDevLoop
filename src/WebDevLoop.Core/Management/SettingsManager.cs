using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Management;

public sealed class SettingsManager(
    ISettingsProfileRepository profiles,
    IRepositoryRecordRepository repositories,
    IEffectiveSettingsProvider effective,
    IUnitOfWork unitOfWork) : ISettingsManager
{
    public async Task<SettingsProfileData> GetGlobalAsync(CancellationToken cancellationToken) =>
        await profiles.GetGlobalAsync(cancellationToken) is { } global ? SettingsProfileMapper.ToData(global) : new SettingsProfileData();

    public async Task<CommandResult<SettingsProfileData>> SaveGlobalAsync(SettingsProfileData data, CancellationToken cancellationToken) =>
        await SaveAsync(await profiles.GetGlobalAsync(cancellationToken), SettingsProfile.ForGlobal, data, cancellationToken);

    public async Task<CommandResult<SettingsProfileData>> GetRepositoryAsync(int repositoryId, CancellationToken cancellationToken)
    {
        if (await repositories.GetAsync(repositoryId, cancellationToken) is null)
        {
            return RepositoryNotFound(repositoryId);
        }

        SettingsProfile? profile = await profiles.FindForRepositoryAsync(repositoryId, cancellationToken);
        return CommandResult<SettingsProfileData>.Succeeded(profile is null ? new SettingsProfileData() : SettingsProfileMapper.ToData(profile));
    }

    public async Task<CommandResult<SettingsProfileData>> SaveRepositoryAsync(int repositoryId, SettingsProfileData data, CancellationToken cancellationToken)
    {
        if (await repositories.GetAsync(repositoryId, cancellationToken) is null)
        {
            return RepositoryNotFound(repositoryId);
        }

        return await SaveAsync(
            await profiles.FindForRepositoryAsync(repositoryId, cancellationToken),
            () => SettingsProfile.ForRepository(repositoryId),
            data,
            cancellationToken);
    }

    public async Task<CommandResult<EffectiveSettingsView>> GetEffectiveAsync(int repositoryId, CancellationToken cancellationToken)
    {
        if (await repositories.GetAsync(repositoryId, cancellationToken) is null)
        {
            return CommandResult<EffectiveSettingsView>.NotFound($"Repository {repositoryId} does not exist.");
        }

        try
        {
            return CommandResult<EffectiveSettingsView>.Succeeded(SettingsProfileMapper.ToView(await effective.GetAsync(repositoryId, cancellationToken)));
        }
        catch (InvalidOperationException exception)
        {
            return CommandResult<EffectiveSettingsView>.Conflict(exception.Message);
        }
    }

    /// <summary>Validates against a throw-away profile of the right scope so an invalid request never mutates the tracked entity.</summary>
    private async Task<CommandResult<SettingsProfileData>> SaveAsync(
        SettingsProfile? existing,
        Func<SettingsProfile> createProfile,
        SettingsProfileData data,
        CancellationToken cancellationToken)
    {
        SettingsProfile candidate = createProfile();
        List<SettingsValidationError> errors = [.. SettingsProfileMapper.Apply(data, candidate)];
        errors.AddRange(SettingsValidator.Validate(candidate));
        if (errors.Count > 0)
        {
            return CommandResult<SettingsProfileData>.Invalid(errors);
        }

        SettingsProfile target = candidate;
        if (existing is null)
        {
            profiles.Add(candidate);
        }
        else
        {
            SettingsProfileMapper.Apply(data, existing);
            target = existing;
        }

        return await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved
            ? CommandResult<SettingsProfileData>.Succeeded(SettingsProfileMapper.ToData(target))
            : CommandResult<SettingsProfileData>.Conflict("The settings were changed concurrently; reload and retry.");
    }

    private static CommandResult<SettingsProfileData> RepositoryNotFound(int repositoryId) =>
        CommandResult<SettingsProfileData>.NotFound($"Repository {repositoryId} does not exist.");
}
