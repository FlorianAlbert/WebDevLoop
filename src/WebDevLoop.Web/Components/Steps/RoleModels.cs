using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Management;

namespace WebDevLoop.Web.Components.Steps;

/// <summary>The model currently configured per role. Steps do not record the model they ran with, so this shows the effective setting.</summary>
public static class RoleModels
{
    public static async Task<IReadOnlyDictionary<AgentRole, string>> LoadAsync(ISettingsManager settings, int repositoryId, CancellationToken cancellationToken)
    {
        CommandResult<EffectiveSettingsView> result = await settings.GetEffectiveAsync(repositoryId, cancellationToken);
        return result is { IsSuccess: true, Value: { } view }
            ? view.Roles.ToDictionary(role => role.Key, role => role.Value.Model)
            : new Dictionary<AgentRole, string>();
    }
}
