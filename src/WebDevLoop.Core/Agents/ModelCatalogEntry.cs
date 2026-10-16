namespace WebDevLoop.Core.Agents;

/// <summary>A Model available through Copilot, with its display name and supported Reasoning Efforts.</summary>
public sealed record ModelCatalogEntry(string Id, string DisplayName, IReadOnlyList<string> SupportedReasoningEfforts)
{
    /// <summary>Matches a selectable Reasoning Effort using ordinal comparison; an empty list supports none.</summary>
    public bool SupportsReasoningEffort(string effort) =>
        SupportedReasoningEfforts.Contains(effort, StringComparer.Ordinal);
}
