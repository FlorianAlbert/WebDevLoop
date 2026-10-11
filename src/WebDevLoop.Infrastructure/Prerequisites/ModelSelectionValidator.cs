using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Infrastructure.Prerequisites;

/// <summary>Validates effective Model selections without loading settings or changing inherited values.</summary>
internal static class ModelSelectionValidator
{
    public static IEnumerable<string> Validate(
        AgentModelSettingsScope scope,
        IReadOnlyDictionary<string, ModelCatalogEntry> models)
    {
        foreach ((AgentRole role, RoleSettings configured) in scope.Settings.Roles)
        {
            if (!models.TryGetValue(configured.Model, out ModelCatalogEntry? model))
            {
                yield return $"{scope.Name}: {role}, Model '{configured.Model}' is not in the Model Catalog.";
            }
            else if (!model.IsReasoningEffortValid(configured.ReasoningEffort))
            {
                yield return $"{scope.Name}: {role}, Model '{configured.Model}', Reasoning Effort '{configured.ReasoningEffort}' is not supported.";
            }
        }
    }
}
