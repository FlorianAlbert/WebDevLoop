namespace WebDevLoop.Core.Agents;

public enum AgentLogKind
{
    Assistant,
    Reasoning,
    ToolStarted,
    ToolCompleted,
    ShellOutput,
    PermissionDenied,
    Error,
}
