using WebDevLoop.Core.Agents;

namespace WebDevLoop.Core.Ports;

/// <summary>
/// Runs Copilot agent turns. The runner enforces <see cref="AgentRunRequest.Policy"/>, registers the role's report tool,
/// applies the role timeout, and returns once the agent reported, failed, timed out, or was cancelled.
/// </summary>
public interface IAgentRunner
{
    /// <summary>Creates the session with <see cref="AgentRunRequest.SessionId"/> and sends the prompt.</summary>
    Task<AgentRunResult> StartAsync(AgentRunRequest request, CancellationToken cancellationToken);

    /// <summary>Resumes the existing session <see cref="AgentRunRequest.SessionId"/> (e.g. a fix turn or after restart) and sends the prompt.</summary>
    Task<AgentRunResult> ResumeAsync(AgentRunRequest request, CancellationToken cancellationToken);

    /// <summary>Aborts the in-flight turn; idempotent for unknown or finished sessions.</summary>
    Task AbortAsync(AgentSessionId sessionId, CancellationToken cancellationToken);
}
