namespace WebDevLoop.Core.Agents;

/// <summary>A Model available through Copilot, with its display name and supported Reasoning Efforts.</summary>
public sealed record ModelCatalogEntry(string Id, string DisplayName, IReadOnlyList<string> SupportedReasoningEfforts)
{
    /// <summary>Whether the Model offers selectable Reasoning Efforts.</summary>
    public bool SupportsReasoning => SupportedReasoningEfforts.Count > 0;

    /// <summary>Matches a selectable Reasoning Effort using ordinal comparison, not configured-value validity.</summary>
    public bool SupportsReasoningEffort(string effort) =>
        SupportedReasoningEfforts.Contains(effort, StringComparer.Ordinal);

    /// <summary>A configured Reasoning Effort is valid if listed, or if the Model lists no Reasoning Efforts.</summary>
    public bool IsReasoningEffortValid(string effort) =>
        !SupportsReasoning || SupportsReasoningEffort(effort);
}
