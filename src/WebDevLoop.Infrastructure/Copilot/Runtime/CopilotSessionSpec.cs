using WebDevLoop.Core.Agents;

namespace WebDevLoop.Infrastructure.Copilot.Runtime;

/// <summary>SDK-independent description of a session; the SDK adapter maps it to session/resume configs.</summary>
internal sealed record CopilotSessionSpec(
    AgentSessionId SessionId,
    string WorkingDirectory,
    AgentModelSettings Settings,
    IReadOnlyList<string> SkillDirectories,
    CopilotToolSelection Tools,
    CopilotReportTool ReportTool,
    Func<AgentPermissionRequest, AgentPermissionDecision> AuthorizePermission,
    CopilotSessionAuth Auth,
    Action<CopilotSessionEvent> OnEvent);
