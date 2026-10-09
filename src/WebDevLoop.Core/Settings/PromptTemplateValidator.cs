using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Settings;

/// <summary>Validates role prompt templates against the closed placeholder set.</summary>
public static class PromptTemplateValidator
{
    public static IReadOnlyList<SettingsValidationError> Validate(AgentRole role, string template)
    {
        string field = FieldName(role);
        IReadOnlySet<string> available = PromptPlaceholders.AvailableFor(role);
        var errors = new List<SettingsValidationError>();

        foreach (string name in PromptTemplateSyntax.PlaceholderNames(template))
        {
            if (!PromptPlaceholders.IsKnown(name))
            {
                errors.Add(new SettingsValidationError(field, $"Unknown placeholder {{{name}}}."));
            }
            else if (!available.Contains(name))
            {
                errors.Add(new SettingsValidationError(field, $"Placeholder {{{name}}} is not available for the {role} role."));
            }
        }

        return errors;
    }

    public static string FieldName(AgentRole role) => $"Roles.{role}.PromptTemplate";
}
