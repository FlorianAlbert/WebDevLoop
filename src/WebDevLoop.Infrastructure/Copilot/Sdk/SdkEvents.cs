using GitHub.Copilot;
using WebDevLoop.Infrastructure.Copilot.Runtime;

namespace WebDevLoop.Infrastructure.Copilot.Sdk;

internal static class SdkEvents
{
    private const int UnauthorizedStatusCode = 401;

    /// <summary>The session event the runner cares about, or null for events it ignores.</summary>
    public static CopilotSessionEvent? Map(SessionEvent sessionEvent) => sessionEvent switch
    {
        AssistantMessageEvent { Data.Content: { Length: > 0 } content } => new(CopilotSessionEventKind.AssistantMessage, content),
        AssistantReasoningEvent { Data.Content: { Length: > 0 } content } => new(CopilotSessionEventKind.Reasoning, content),
        ToolExecutionStartEvent start => new(CopilotSessionEventKind.ToolStarted, start.Data.ToolName ?? string.Empty),
        ToolExecutionCompleteEvent complete => new(
            CopilotSessionEventKind.ToolCompleted,
            $"{complete.Data.ToolCallId}: {(complete.Data.Success ? "succeeded" : "failed")}"),
        ToolShellOutputEvent output => new(CopilotSessionEventKind.ShellOutput, output.Data.Text ?? string.Empty),
        SessionErrorEvent error => new(
            CopilotSessionEventKind.Error,
            error.Data.Message ?? error.Data.ErrorType ?? "Copilot session error.",
            error.Data.StatusCode == UnauthorizedStatusCode || SdkFailures.IsAuthenticationMessage(error.Data.ErrorType)),
        SessionIdleEvent => new(CopilotSessionEventKind.Idle, string.Empty),
        _ => null,
    };
}
