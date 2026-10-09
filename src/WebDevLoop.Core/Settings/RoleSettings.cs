namespace WebDevLoop.Core.Settings;

/// <summary>Effective, fully resolved settings for one agent role.</summary>
public sealed record RoleSettings(string Model, string ReasoningEffort, string PromptTemplate, TimeSpan Timeout);
