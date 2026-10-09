namespace WebDevLoop.Core.Agents;

/// <summary>Identifies one Copilot runtime: an auth identity at a specific token generation.</summary>
public sealed record CopilotRuntimeKey(CopilotAuthIdentity Identity, int TokenGeneration, DateTimeOffset? ExpiresAt);
