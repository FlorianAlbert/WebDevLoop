using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Settings;

/// <summary>A valid template could not be rendered because the caller did not supply every used placeholder.</summary>
public sealed class PromptRenderingException(AgentRole role, IReadOnlyList<string> missingPlaceholders)
    : InvalidOperationException(
        $"Cannot render the {role} prompt: no value supplied for "
        + string.Join(", ", missingPlaceholders.Select(name => $"{{{name}}}")) + ".")
{
    public IReadOnlyList<string> MissingPlaceholders { get; } = missingPlaceholders;
}
