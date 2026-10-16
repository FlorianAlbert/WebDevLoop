using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Infrastructure.Prerequisites;

/// <summary>Loads the effective agent settings for global settings and every registered repository in one scoped read.</summary>
public sealed class AgentModelSettingsReader(
    ISettingsProfileRepository profiles,
    IRepositoryRecordRepository repositories,
    SettingsResolver resolver)
{
    public async Task<IReadOnlyList<AgentModelSettingsScope>> ReadAsync(CancellationToken cancellationToken)
    {
        SettingsProfile global = await profiles.GetGlobalAsync(cancellationToken)
            ?? throw new InvalidOperationException("The global settings have not been seeded.");
        List<AgentModelSettingsScope> settings = [new("Global settings", resolver.Resolve(global))];
        foreach (RepositoryRecord repository in await repositories.ListAsync(cancellationToken))
        {
            SettingsProfile? overrides = await profiles.FindForRepositoryAsync(repository.Id, cancellationToken);
            settings.Add(new(repository.Ref.ToString(), resolver.Resolve(global, overrides)));
        }

        return settings;
    }
}

/// <summary>Effective agent settings and their diagnostic scope name.</summary>
public sealed record AgentModelSettingsScope(string Name, EffectiveSettings Settings);
