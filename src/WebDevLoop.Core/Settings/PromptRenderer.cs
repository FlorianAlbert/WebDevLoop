using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Settings;

/// <summary>Renders role prompt templates by substituting known placeholders only.</summary>
public sealed class PromptRenderer
{
    /// <exception cref="SettingsValidationException">The template uses a placeholder unknown or unavailable for the role.</exception>
    /// <exception cref="PromptRenderingException">A placeholder used by the template has no value.</exception>
    public string Render(AgentRole role, string template, IReadOnlyDictionary<string, string> values)
    {
        IReadOnlyList<SettingsValidationError> errors = PromptTemplateValidator.Validate(role, template);
        if (errors.Count > 0)
        {
            throw new SettingsValidationException(errors);
        }

        string[] missing = PromptTemplateSyntax.PlaceholderNames(template)
            .Where(name => !values.ContainsKey(name))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new PromptRenderingException(role, missing);
        }

        // Single pass: substituted values (e.g. issue bodies) are never re-scanned for placeholders.
        return PromptTemplateSyntax.Placeholder().Replace(
            template,
            match => values[match.Groups["name"].Value]);
    }
}
