namespace WebDevLoop.Infrastructure.Copilot.Runtime;

internal enum AgentPermissionKind
{
    Shell,
    Read,
    Write,
    Url,
    Mcp,
    CustomTool,
    Other,
}

/// <param name="Target">The command line, path, URL, or tool name the agent wants to use.</param>
/// <param name="RawKind">The runtime's permission kind, for logs.</param>
internal sealed record AgentPermissionRequest(AgentPermissionKind Kind, string Target, string RawKind);

internal sealed record AgentPermissionDecision(bool IsApproved, string? Reason)
{
    public static AgentPermissionDecision Approve() => new(true, null);

    public static AgentPermissionDecision Deny(string reason) => new(false, reason);
}
