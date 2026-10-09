namespace WebDevLoop.Core.Domain;

public sealed record RoleSettingsOverride(
    string? Model = null,
    string? ReasoningEffort = null,
    string? PromptTemplate = null,
    int? TimeoutSeconds = null);
