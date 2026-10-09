namespace WebDevLoop.Infrastructure.Copilot.Runtime;

internal enum CopilotSessionEventKind
{
    AssistantMessage,
    Reasoning,
    ToolStarted,
    ToolCompleted,
    ShellOutput,

    /// <summary>The turn failed; see <see cref="CopilotSessionEvent.IsAuthenticationFailure"/>.</summary>
    Error,

    /// <summary>The turn ended and the session waits for the next prompt.</summary>
    Idle,
}

internal sealed record CopilotSessionEvent(CopilotSessionEventKind Kind, string Text, bool IsAuthenticationFailure = false);
