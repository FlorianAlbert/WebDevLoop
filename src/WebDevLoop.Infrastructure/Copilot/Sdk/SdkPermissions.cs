#pragma warning disable GHCP001 // PermissionDecision is marked experimental in the SDK but is the only permission-handler result type.
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using WebDevLoop.Infrastructure.Copilot.Runtime;

namespace WebDevLoop.Infrastructure.Copilot.Sdk;

internal static class SdkPermissions
{
    public static Func<PermissionRequest, PermissionInvocation, Task<PermissionDecision>> CreateHandler(
        Func<AgentPermissionRequest, AgentPermissionDecision> authorize) =>
        (request, _) =>
        {
            AgentPermissionDecision decision = authorize(ToAgentRequest(request));
            return Task.FromResult(decision.IsApproved ? PermissionDecision.ApproveOnce() : PermissionDecision.Reject(decision.Reason));
        };

    public static AgentPermissionRequest ToAgentRequest(PermissionRequest request) => request switch
    {
        PermissionRequestShell shell => new(AgentPermissionKind.Shell, shell.FullCommandText ?? string.Empty, shell.Kind),
        PermissionRequestRead read => new(AgentPermissionKind.Read, read.ResolvedPath ?? read.Path ?? string.Empty, read.Kind),
        PermissionRequestWrite write => new(AgentPermissionKind.Write, write.FileName ?? string.Empty, write.Kind),
        PermissionRequestUrl url => new(AgentPermissionKind.Url, url.Url ?? string.Empty, url.Kind),
        PermissionRequestMcp mcp => new(AgentPermissionKind.Mcp, $"{mcp.ServerName}/{mcp.ToolName}", mcp.Kind),
        PermissionRequestCustomTool tool => new(AgentPermissionKind.CustomTool, tool.ToolName ?? string.Empty, tool.Kind),
        _ => new(AgentPermissionKind.Other, request.Kind ?? "unknown", request.Kind ?? "unknown"),
    };
}
