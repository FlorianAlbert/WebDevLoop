namespace WebDevLoop.Infrastructure.Copilot.Runtime;

/// <summary>A resume found no persisted Copilot session with the requested id (e.g. its state was deleted).</summary>
public sealed class CopilotSessionNotFoundException(string sessionId)
    : Exception($"The Copilot session '{sessionId}' does not exist and cannot be resumed.")
{
    public string SessionId { get; } = sessionId;
}
