namespace WebDevLoop.Core.Agents;

/// <summary>A Model available through Copilot, with its display name and supported Reasoning Efforts.</summary>
public sealed record ModelCatalogEntry(string Id, string DisplayName, IReadOnlyList<string> SupportedReasoningEfforts);
