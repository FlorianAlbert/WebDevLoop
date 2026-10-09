namespace WebDevLoop.Infrastructure.Copilot;

/// <summary>Copilot runtime built-in tool names that roles may be granted. Anything not listed (web, sub-agents, ask-user, MCP) stays off.</summary>
internal static class CopilotBuiltInTools
{
    public static readonly IReadOnlyList<string> Always = ["skill"];

    public static readonly IReadOnlyList<string> Read = ["view", "grep", "glob"];

    public static readonly IReadOnlyList<string> Write = ["create", "edit", "apply_patch"];

    public static readonly IReadOnlyList<string> Shell =
    [
        "bash", "read_bash", "write_bash", "stop_bash", "list_bash",
        "powershell", "read_powershell", "write_powershell", "stop_powershell", "list_powershell",
    ];
}
