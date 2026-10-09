namespace WebDevLoop.Core.Agents;

/// <param name="Id">Installation id or user login.</param>
public sealed record CopilotAuthIdentity(CopilotAuthKind Kind, string Id);
